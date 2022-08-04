/// HTTP Client used by LibHttpBytesClient StdLib functions
/// TODO: consider inlining this into LibHttpBytesClient
module BackendOnlyStdLib.HttpBytesClient

open System.Threading.Tasks
open FSharp.Control.Tasks

open System.IO
open System.Net.Http

open Prelude
open LibExecution
open LibExecution.RuntimeTypes
open LibBackend
open VendoredTablecloth

module Telemetry = LibService.Telemetry

type private HttpResult = { body : byte []; code : int; headers : HttpHeaders.T }
type private ClientError = { error : string; code : int option }

// There has been quite a history of .NET's HttpClient having problems,
// including socket exhaustion and DNS results not expiring.
// The history is outlined well here:
// https://www.stevejgordon.co.uk/httpclient-connection-pooling-in-dotnet-core
//
// As of .NET 6 it seems we no longer need to worry about either socket
// exhaustion or DNS issues. It appears that we can use either multiple HTTP
// clients or just one, we use just one for efficiency.
// See https://docs.microsoft.com/en-us/aspnet/core/fundamentals/http-requests?view=aspnetcore-6.0#alternatives-to-ihttpclientfactory
//
// Note that the number of sockets was verified manually, with:
// `sudo netstat -apn | grep _WAIT`
// TODO: I don't see where "the nunber of sockets" is actually configured?
let private socketHandler : HttpMessageHandler =
  new SocketsHttpHandler(
    // Avoid DNS problems
    PooledConnectionIdleTimeout = System.TimeSpan.FromMinutes 5.0,
    PooledConnectionLifetime = System.TimeSpan.FromMinutes 10.0,

    AutomaticDecompression = System.Net.DecompressionMethods.None,

    AllowAutoRedirect = false,

    UseProxy = true,
    Proxy = System.Net.WebProxy(Config.httpclientProxyUrl, false),

    // Don't add a RequestId header for opentelemetry
    ActivityHeadersPropagator = null,

    // Users share the HttpClient, don't let them share cookies!
    UseCookies = false
  )

let private httpClient : HttpClient =
  new HttpClient(
    socketHandler,
    disposeHandler = false,

    // These seem like reasonable defaults
    Timeout = System.TimeSpan.FromSeconds 30.0,
    MaxResponseContentBufferSize = 1024L * 1024L * 100L // 100MB
  )

let private httpCall
  (url : string)
  (queryParams : (string * string list) list)
  (method : HttpMethod)
  (reqHeaders : HttpHeaders.T)
  (reqBody : byte array)
  : Task<Result<HttpResult, ClientError>> =
  task {
    use _ =
      Telemetry.child
        "HttpClient.call"
        [ "request.url", url; "request.method", method ]
    try
      let uri = System.Uri(url, System.UriKind.Absolute)

      // currently we only support http(s) requests
      if uri.Scheme <> "https" && uri.Scheme <> "http" then
        return Error { code = None; error = "Unsupported protocol" }
      else
        let reqUri =
          let queryString =
            let query =
              // Remove the question mark
              if uri.Query.Length > 0 then String.dropLeft 1 uri.Query else uri.Query
            HttpQueryEncoding.createQueryString query queryParams

          System.UriBuilder(
            Scheme = uri.Scheme,
            Host = uri.Host,
            Port = uri.Port,
            Path = uri.AbsolutePath,
            Query = queryString
          )

        use req =
          new HttpRequestMessage(
            method,
            string reqUri,
            Version = System.Net.HttpVersion.Version30,
            Content = new ByteArrayContent(reqBody)
          )

        // headers
        // TODO is it still appropriate to include these default headers?
        let defaultHeaders =
          Map [ "Accept", "*/*"; "Accept-Encoding", "deflate, gzip, br" ]

        Map reqHeaders
        |> Map.mergeFavoringRight defaultHeaders
        |> Map.iter (fun k v ->
          // TODO do we need this branch?
          // I think so - .NET is odd with content types, to my memory
          if String.equalsCaseInsensitive k "content-type" then
            try
              req.Content.Headers.ContentType <-
                Headers.MediaTypeHeaderValue.Parse(v)
            with
            | :? System.FormatException ->
              Exception.raiseCode "Invalid content-type header"
          else
            // Dark headers can only be added once, as they use a Dict.
            // Remove them so they don't get added twice.
            // TODO: re-evaluate if we need this. See logic in HttpClient.
            // Also, consider that we could (do, in this draft) return headers
            // as a list of tuples, so the Dict claim is false currently.
            req.Headers.Remove(k) |> ignore<bool>
            let added = req.Headers.TryAddWithoutValidation(k, v)

            // Headers are split between req.Headers and req.Content.Headers so just try both
            if not added then
              req.Content.Headers.Remove(k) |> ignore<bool>
              req.Content.Headers.Add(k, v))

        // send request
        Telemetry.addTag "request.content_type" req.Content.Headers.ContentType
        Telemetry.addTag "request.content_length" req.Content.Headers.ContentLength
        use! response = httpClient.SendAsync req

        Telemetry.addTags [ "response.status_code", response.StatusCode
                            "response.version", response.Version ]
        use! responseStream = response.Content.ReadAsStreamAsync()
        use memoryStream = new MemoryStream()
        do! responseStream.CopyToAsync(memoryStream)
        let respBody = memoryStream.ToArray()

        let headers =
          response
          |> HttpHeaders.headersForAspNetResponse
          // TODO Should we really be manipulating (lowercasing) these?
          |> List.map (fun (k : string, v) -> (String.toLowercase k, v))

        return
          { body = respBody; code = int response.StatusCode; headers = headers }
          |> Ok
    with
    | :? TaskCanceledException -> // only timeouts
      Telemetry.addTags [ "error", true; "error.msg", "Timeout" ]
      return Error { code = None; error = "Timeout" }
    | :? System.ArgumentException as e -> // incorrect protocol, possibly more
      let message =
        if e.Message = "Only 'http' and 'https' schemes are allowed. (Parameter 'value')" then
          "Unsupported protocol"
        else
          e.Message
      Telemetry.addTags [ "error", true; "error.msg", message ]
      return Error { code = None; error = message }
    | :? System.UriFormatException ->
      Telemetry.addTags [ "error", true; "error.msg", "Invalid URI" ]
      return Error { code = None; error = "Invalid URI" }
    | :? IOException as e -> return Error { code = None; error = e.Message }
    | :? HttpRequestException as e ->
      let code = if e.StatusCode.HasValue then int e.StatusCode.Value else 0
      Telemetry.addException [ "error.status_code", code ] e
      let message = e |> Exception.getMessages |> String.concat " "
      return Error { code = Some code; error = message }
  }

let sendRequest
  (uri : string)
  (verb : HttpMethod)
  (reqBody : byte array)
  (query : List<string * List<string>>)
  (reqHeaders : List<string * string>)
  : Ply<Dval> =
  uply {
    match! httpCall uri query verb reqHeaders reqBody with
    | Ok response ->
      let parsedResponseHeaders =
        response.headers

        // TODO should we really be trimming these?
        |> List.map (fun (k, v) -> (String.trim k, String.trim v))

        // TODO should we really filter out headers of blank keys?
        |> List.filter (fun (k, _) -> String.length k > 0)

        |> List.map (fun (k, v) -> DTuple(DStr k, DStr v, []))
        |> DList

      return
        [ ("body", DBytes response.body)
          ("headers", parsedResponseHeaders)
          ("code", DInt(int64 response.code)) ]
        |> Dval.obj
        |> Ok
        |> DResult
    | Error err -> return DResult(Error(DStr err.error))
  }
