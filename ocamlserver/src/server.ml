module Clu = Cohttp_lwt_unix
module C = Cohttp
module S = Clu.Server
module Request = Clu.Request
module Header = C.Header

let p s = Printf.printf s;; flush stdout;;

let server =
  let callback _ req body =
    let () = p "tests" in
    let uri = req |> Request.uri in
    (* let meth = req |> Request.meth |> Code.string_of_method in *)
    (* let headers = req |> Request.headers |> Header.to_string in *)
    let auth = req |> Request.headers |> Header.get_authorization in

    let admin_rpc_handler () =
      let g = Graph.load "blog"
      in
      Graph.to_frontend g |> Yojson.Basic.to_string
    in

    let auth_handler handler
      = match auth with
      | (Some `Basic ("dark", "2DqMHguUfsAGCPerWgyHRxPi"))
        -> handler
      | _
        -> Cohttp_lwt_unix.Server.respond_need_auth (`Basic "dark") () in

    let route_handler handler =
      let body =
        match (Uri.path uri) with
        | "/admin/api/rpc" -> admin_rpc_handler ()
        | "/sitemap.xml" -> "sitemap"
        | "/favicon.ico" -> "favicon"
        | "/admin/ui" -> "admin ui"
        | _ -> "app routing" in

      let debug = Uri.path uri in
      S.respond_string ~status:`OK ~body:(Printf.sprintf "%s - '%s'" body debug) () in

    ()
    |> route_handler
    |> auth_handler
  in
  S.create ~mode:(`TCP (`Port 8000)) (S.make ~callback ())

let run () = ignore (Lwt_main.run server)
