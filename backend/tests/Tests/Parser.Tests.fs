module Tests.Parser

open Expecto

open Prelude
open TestUtils.TestUtils

module PT = LibExecution.ProgramTypes
module PTParser = LibExecution.ProgramTypesParser
module RT = LibExecution.RuntimeTypes
module PT2RT = LibExecution.ProgramTypesToRuntimeTypes
module NR = LibParser.NameResolver

let id = 0UL // since we're ignoring IDs, just use the same one everywhere

let exprRTs =
  let t name testStr expectedExpr =
    testTask name {
      let! actual =
        LibParser.Parser.parseRTExpr
          localBuiltIns
          packageManager
          NR.OnMissing.Allow
          "parser.tests.fs"
          testStr
        |> Ply.toTask
      let expectedExpr = PT2RT.Expr.toRT expectedExpr
      return Expect.equalExprIgnoringIDs actual expectedExpr
    }


  testList
    "Exprs"
    // TODO: order these by simplicity, and add more tests
    [
      // First, let's start with some simple ones
      // that don't have any dependencies.
      t "zero" "0.0" (PT.EFloat(id, Positive, "0", "0"))

      t "negative zero" "(-0.0)" (PT.EFloat(id, Negative, "0", "0"))

      t "negative 180" "-180.0" (PT.EFloat(id, Negative, "180", "0"))

      t
        "10 cents"
        "82.10"
        (PT.EFloat(
          id,
          Positive,
          "82",
          "099999999999994315658113919198513031005859375"
        ))

      t
        "simple expr"
        "(5L + 3L) == 8L"
        (PT.EInfix(
          id,
          PT.InfixFnCall(PT.ComparisonEquals),
          PT.EInfix(
            id,
            (PT.InfixFnCall(PT.ArithmeticPlus)),
            PT.EInt64(id, 5L),
            PT.EInt64(id, 3L)
          ),
          PT.EInt64(id, 8L)
        ))

      t
        "lambdas with 2 args"
        "fun x y -> 8L"
        (PT.ELambda(
          id,
          NEList.doubleton (PT.LPVariable(id, "x")) (PT.LPVariable(id, "y")),
          PT.EInt64(id, 8L)
        ))

      t
        "lambdas with 3 args"
        "fun x y z -> 8L"
        (PT.ELambda(
          id,
          NEList.ofList
            (PT.LPVariable(id, "x"))
            [ PT.LPVariable(id, "y"); PT.LPVariable(id, "z") ],
          PT.EInt64(id, 8L)
        ))

      t
        "lambdas with 4 args"
        "fun a b c d -> 8L"
        (PT.ELambda(
          id,
          NEList.ofList
            (PT.LPVariable(id, "a"))
            [ PT.LPVariable(id, "b")
              PT.LPVariable(id, "c")
              PT.LPVariable(id, "d") ],
          PT.EInt64(id, 8L)
        ))

      // Now let's test some more complex expressions
      t
        "pipe without expr"
        "(let x = 5L\nx |> PACKAGE.Darklang.Stdlib.List.map 5L)"
        (PT.ELet(
          id,
          PT.LPVariable(id, "x"),
          PT.EInt64(id, 5L),
          PT.EPipe(
            id,
            PT.EVariable(id, "x"),
            [ PT.EPipeFnCall(
                id,
                Ok(PT.FQFnName.fqPackage "Darklang" [ "Stdlib"; "List" ] "map"),
                [],
                [ PT.EInt64(id, 5L) ]
              ) ]
          )
        )) ]


let tests = testList "Parser" [ exprRTs ]
