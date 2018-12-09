// Learn more about F# at http://fsharp.org

open System
open Expecto


/// Test Fixture Helpers

type ParameterizedTest<'a> =
    | Sync of string * ('a -> unit)
    | Async of string * ('a -> Async<unit>)

let testCase' name test =
     ParameterizedTest.Sync(name,test)

let testCaseAsync' name test  =
    ParameterizedTest.Async(name,test)

let inline testFixture'<'a> setup =
    Seq.map (fun ( parameterizedTest : ParameterizedTest<'a>) ->
        match parameterizedTest with
        | Sync (name, test) ->
            testCase name <| fun () -> test >> async.Return |> setup |> Async.RunSynchronously
        | Async (name, test) ->
            testCaseAsync name <| setup test

    )


/// Disposable directory 

[<AllowNullLiteral>]
type private DisposableDirectory (directory : string) =
    static member Create() =
        let tempPath = IO.Path.Combine(IO.Path.GetTempPath(), Guid.NewGuid().ToString("n"))
        printfn "Creating directory %s" tempPath
        IO.Directory.CreateDirectory tempPath |> ignore
        new DisposableDirectory(tempPath)
    member x.DirectoryInfo = IO.DirectoryInfo(directory)
    interface IDisposable with
        member x.Dispose() =
            printfn "Deleting directory %s" x.DirectoryInfo.FullName
            IO.Directory.Delete(x.DirectoryInfo.FullName,true)


let withDisposableDirectory (test) = async {
  use disposableDirectory = DisposableDirectory.Create()
  do! test disposableDirectory.DirectoryInfo
}

let disposableDirectoryTests = [
  testCase' "Write file to directory" <| fun (directory : IO.DirectoryInfo) ->
        let fileName = IO.Path.Combine(directory.FullName, "Foo.txt")
        let contents = [
          "hello"
          "world"
        ]
        IO.File.WriteAllLines(fileName,contents)
  testCaseAsync' "Write file to directory async" <| fun (directory : IO.DirectoryInfo) -> async {
        let fileName = IO.Path.Combine(directory.FullName, "Foo.txt")
        let contents = [
          "hello"
          "mars"
        ]
        do! IO.File.WriteAllLinesAsync(fileName,contents) |> Async.AwaitTask
  }
]


[<Tests>]
let tests = 
  testList "Disposable Directory tests" [
    yield! testFixture' withDisposableDirectory disposableDirectoryTests 
  ]

[<EntryPoint>]
let main argv =
  runTestsInAssembly defaultConfig argv