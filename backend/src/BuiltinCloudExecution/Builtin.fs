/// Builtin functions that can only be run on the backend
///
/// Aggregates functions in other modules
module BuiltinCloudExecution.Builtin

module Builtin = LibExecution.Builtin

let fnRenames : Builtin.FnRenames =
  // old names, new names
  // eg: fn "Http" "respond" 0, fn "Http" "response" 0
  []

let builtins =
  Builtin.combine
    [ Libs.DB.builtins; Libs.Event.builtins; Libs.Packages.builtins ]
    fnRenames
