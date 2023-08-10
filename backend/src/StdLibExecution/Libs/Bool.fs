module StdLibExecution.Libs.Bool

open System.Threading.Tasks
open FSharp.Control.Tasks
open Prelude
open LibExecution.RuntimeTypes
open LibExecution.StdLib.Shortcuts

let fn = fn [ "Bool" ]

let varA = TVariable "a"

let types : List<BuiltInType> = []
let constants : List<BuiltInConstant> = []

let fns : List<BuiltInFn> =
  [ { name = fn "not" 0
      typeParams = []
      parameters = [ Param.make "b" TBool "" ]
      returnType = TBool
      description =
        "Returns the inverse of <param b>: {{true}} if <param b> is {{false}} and {{false}} if <param b> is {{true}}"
      fn =
        (function
        | _, _, [ DBool b ] -> Ply(DBool(not b))
        | _ -> incorrectArgs ())
      sqlSpec = SqlFunction "not"
      previewable = Pure
      deprecated = NotDeprecated }


    { name = fn "and" 0
      typeParams = []
      parameters = [ Param.make "a" TBool ""; Param.make "b" TBool "" ]
      returnType = TBool
      description = "Returns {{true}} if both <param a> and <param b> are {{true}}"
      fn =
        (function
        | _, _, [ DBool a; DBool b ] -> Ply(DBool(a && b))
        | _ -> incorrectArgs ())
      sqlSpec = SqlBinOp "AND"
      previewable = Pure
      deprecated = NotDeprecated }


    { name = fn "or" 0
      typeParams = []
      parameters = [ Param.make "a" TBool ""; Param.make "b" TBool "" ]
      returnType = TBool
      description =
        "Returns {{true}} if either <param a> is true or <param b> is {{true}}"
      fn =
        (function
        | _, _, [ DBool a; DBool b ] -> Ply(DBool(a || b))
        | _ -> incorrectArgs ())
      sqlSpec = SqlBinOp "OR"
      previewable = Pure
      deprecated = NotDeprecated } ]

let contents = (fns, types, constants)
