/// For executing code with the appropriate production "Dark cloud" execution,
/// setting traces, stdlib, etc, appropriately.
/// Used by any "Cloud" service (bwdserver, queueworker, cronchecker, etc.)
module LibCloudExecution.CloudExecution

open FSharp.Control.Tasks
open System.Threading.Tasks

open Prelude

module RT = LibExecution.RuntimeTypes
module Dval = LibExecution.Dval
module PT = LibExecution.ProgramTypes
module PT2RT = LibExecution.ProgramTypesToRuntimeTypes
module AT = LibExecution.AnalysisTypes
module Exe = LibExecution.Execution

open LibCloud


let builtins : RT.Builtins =
  LibExecution.Builtin.combine
    [ BuiltinExecution.Builtin.builtins HttpClient.configuration
      BuiltinCloudExecution.Builtin.builtins
      BuiltinDarkInternal.Builtin.builtins ]
    []

let packageManager = PackageManager.packageManager

let createState
  (traceID : AT.TraceID.T)
  (program : RT.Program)
  (tracing : RT.Tracing)
  : Task<RT.ExecutionState> =
  task {
    let extraMetadata (state : RT.ExecutionState) : Metadata =
      let tlid, id = Option.defaultValue (0UL, 0UL) state.tracing.caller
      [ "callerTLID", tlid
        "callerID", id
        "traceID", traceID
        "canvasID", program.canvasID ]

    let notify (state : RT.ExecutionState) (msg : string) (metadata : Metadata) =
      let metadata = extraMetadata state @ metadata
      LibService.Rollbar.notify msg metadata

    let sendException (state : RT.ExecutionState) (metadata : Metadata) (exn : exn) =
      let metadata = extraMetadata state @ metadata
      LibService.Rollbar.sendException None metadata exn

    return
      Exe.createState builtins packageManager tracing sendException notify program
  }

type ExecutionReason =
  /// The first time a trace is executed. This means more data should be stored and
  /// more users notified.
  | InitialExecution of PT.Handler.HandlerDesc * varname : string * RT.Dval

  /// A reexecution is a trace that already exists, being amended with new values
  | ReExecution

/// Execute handler.
/// This could be
/// - the first execution, which will
///   - have an ExecutionReason of InitialExecution
///   - initialize traces
///   - send pushes
/// - or ReExecution, which will
///   - update existing traces
///   - not send pushes
let executeHandler
  (pusherSerializer : Pusher.PusherEventSerializer)
  (h : RT.Handler.T)
  (program : RT.Program)
  (traceID : AT.TraceID.T)
  (inputVars : Map<string, RT.Dval>)
  (reason : ExecutionReason)
  : Task<RT.Dval * Tracing.TraceResults.T> =
  task {
    let tracing = Tracing.create program.canvasID h.tlid traceID

    match reason with
    | InitialExecution(desc, varname, inputVar) ->
      tracing.storeTraceInput desc varname inputVar
    | ReExecution -> ()

    let! state = createState traceID program tracing.executionTracing
    HashSet.add h.tlid tracing.results.tlids
    let! result = Exe.executeExpr state h.tlid inputVars h.ast

    let findPackageBody (tlid : tlid) : Ply<Option<string * RT.Expr>> =
      packageManager.getFnByTLID tlid
      |> Ply.map (
        Option.map (fun (pkg : RT.PackageFn.T) -> string pkg.name, pkg.body)
      )

    let sourceOf (tlid : tlid) (id : id) : Ply<string> =
      uply {
        let! data = findPackageBody tlid
        let mutable result = "unknown caller", "unknown body", "unknown expr"
        match data with
        | None -> ()
        | Some(fnName, e) ->
          LibExecution.RuntimeTypesAst.preTraversal
            (fun expr ->
              if RT.Expr.toID expr = id then result <- fnName, string e, string expr
              expr)
            identity
            identity
            identity
            identity
            identity
            identity
            e
          |> ignore<RT.Expr>
        let (fnName, _body, expr) = result
        return $"fn {fnName}\nexpr:\n{expr}\n"
      }

    let sourceString (source : RT.Source) : Ply<string> =
      match source with
      | None -> Ply "No source"
      | Some(tlid, id) -> sourceOf tlid id

    let error (msg : string) : RT.Dval =
      let typeName =
        RT.FQTypeName.fqPackage "Darklang" [ "Stdlib"; "Http" ] "Response"

      let fields =
        [ ("statusCode", RT.DInt64 500)
          ("headers",
           [] |> Dval.list (RT.KTTuple(RT.ValueType.string, RT.ValueType.string, [])))
          ("body", msg |> UTF8.toBytes |> Dval.byteArrayToDvalList) ]

      RT.DRecord(typeName, typeName, [], Map fields)

    // CLEANUP This is a temporary hack to make it easier to work on local dev
    // servers. We should restrict this to dev mode only
    let! result =
      task {
        match result with
        | Ok result -> return result
        | Error(originalSource, originalRTE) ->
          let! originalSource = sourceString originalSource
          match! Exe.runtimeErrorToString state originalRTE with
          | Ok(RT.DString msg) ->
            let msg = $"Error: {msg}\n\nSource: {originalSource}"
            return error msg
          | Ok result -> return result
          | Error(firstErrorSource, firstErrorRTE) ->
            let! firstErrorSource = sourceString firstErrorSource
            match! Exe.runtimeErrorToString state firstErrorRTE with
            | Ok(RT.DString msg) ->
              return
                error (
                  $"An error occured trying to print a runtime error."
                  + $"\n\nThe formatting error occurred in {firstErrorSource}. The error was:\n{msg}"
                  + $"\n\nThe original error is ({originalSource}) {originalRTE}"
                )
            | Ok result -> return result
            | Error(secondErrorSource, secondErrorRTE) ->
              let! secondErrorSource = sourceString secondErrorSource
              return
                error (
                  $"Two errors occured trying to print a runtime error."
                  + $"\n\nThe 2nd formatting error occurred in {secondErrorSource}. The error was:\n{secondErrorRTE}"
                  + $"\n\nThe first formatting error occurred in {firstErrorSource}. The error was:\n{firstErrorRTE}"
                  + $"\n\nThe original error is ({originalSource}) {originalRTE}"
                )
      }

    tracing.storeTraceResults ()

    match reason with
    | ReExecution -> ()
    | InitialExecution _ ->
      if tracing.enabled then
        let tlids = HashSet.toList tracing.results.tlids
        Pusher.push
          pusherSerializer
          program.canvasID
          (Pusher.NewTrace(traceID, tlids))
          None

    return (result, tracing.results)
  }

/// We call this reexecuteFunction because it always runs in an existing trace.
let reexecuteFunction
  (canvasID : CanvasID)
  (program : RT.Program)
  (callerTLID : tlid)
  (callerID : id)
  (traceID : AT.TraceID.T)
  (rootTLID : tlid)
  (name : RT.FQFnName.FQFnName)
  (typeArgs : List<RT.TypeReference>)
  (args : NEList<RT.Dval>)
  : Task<RT.ExecutionResult * Tracing.TraceResults.T> =
  task {
    // FIX - the TLID here is the tlid of the toplevel in which the call exists, not
    // the rootTLID of the trace.
    let tracing = Tracing.create canvasID rootTLID traceID
    let! state = createState traceID program tracing.executionTracing
    let source = Some(callerTLID, callerID)
    let! result = Exe.executeFunction state source name typeArgs args
    tracing.storeTraceResults ()
    return result, tracing.results
  }


/// Ensure library is ready to be called. Throws if it cannot initialize.
let init () : Task<unit> =
  task {
    do! packageManager.init
    return ()
  }
