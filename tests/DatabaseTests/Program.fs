// Learn more about F# at http://fsharp.org

open System
open Expecto
open Npgsql
open SimpleMigrations
open SimpleMigrations.DatabaseProvider
open System.Reflection
open Npgsql
open Npgsql

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


module DatabaseTestHelpers =
    let execNonQuery connStr commandStr =
        use conn = new NpgsqlConnection(connStr)
        use cmd = new NpgsqlCommand(commandStr,conn)
        conn.Open()
        cmd.ExecuteNonQuery()

    let createDatabase connStr databaseName =
        databaseName
        |> sprintf "CREATE database \"%s\" ENCODING = 'UTF8'"
        |> execNonQuery connStr
        |> ignore

    let dropDatabase connStr databaseName =
        //Drop all connections to postgres can drop the database
        databaseName
        |> sprintf "select pg_terminate_backend(pid) from pg_stat_activity where datname='%s';"
        |> execNonQuery connStr
        |> ignore

        databaseName
        |> sprintf "DROP database \"%s\""
        |> execNonQuery connStr
        |> ignore
    let cloneConnectionString (conn : NpgsqlConnectionStringBuilder) =
        conn
        |> string 
        |> NpgsqlConnectionStringBuilder

/// Disposable database 

type private DisposableDatabase (superConn : NpgsqlConnectionStringBuilder, databaseName : string) =
    static member Create(connStr) =
        let databaseName = System.Guid.NewGuid().ToString("n")
        DatabaseTestHelpers.createDatabase (connStr |> string) databaseName
        new DisposableDatabase(connStr,databaseName)
    member x.SuperConn = superConn
    member x.Conn =
        let builder = x.SuperConn |> DatabaseTestHelpers.cloneConnectionString
        builder.Database <- x.DatabaseName
        builder
    member x.DatabaseName = databaseName
    interface IDisposable with
        member x.Dispose() =
            DatabaseTestHelpers.dropDatabase (superConn |> string) databaseName


let withDisposableDatabase connectionString runMigration (test) = async {
  use disposableDirectory = DisposableDatabase.Create(connectionString)
  runMigration disposableDirectory.Conn
  do! test disposableDirectory.Conn
}

let disposableDirectoryTests = [
  testCase' "Write to database" <| fun (connStr : NpgsqlConnectionStringBuilder) ->
        use conn = new NpgsqlConnection(connStr.ToString())
        use cmd = new NpgsqlCommand("INSERT INTO animals (name, birthday) VALUES ('Spunky', now())", conn)
        let added = cmd.ExecuteNonQuery()
        Expect.equal added 1 "Should have added 1 row"
       
  testCaseAsync' "Read count from database" <| fun (connStr : NpgsqlConnectionStringBuilder) -> async {
        use conn = new NpgsqlConnection(connStr.ToString())
        use cmd = new NpgsqlCommand("SELECT COUNT(*) FROM animals)", conn)
        let! reader = cmd.ExecuteReaderAsync() |> Async.AwaitTask
        let! canRead = reader.ReadAsync() |> Async.AwaitTask
        let count = reader.GetInt64(0)
        Expect.equal count 0L "Should have 0 rows"
  }
]


let runMigration (migrationsAssembly : Assembly) (connStr : NpgsqlConnectionStringBuilder) =
    use connection = new NpgsqlConnection(connStr.ToString())
    let databaseProvider = new PostgresqlDatabaseProvider(connection)
    let migrator = new SimpleMigrator(migrationsAssembly, databaseProvider)
    migrator.Load()
    migrator.MigrateToLatest()

let getEnvOrDefault defaultVal str =
    let envVar = System.Environment.GetEnvironmentVariable str
    if String.IsNullOrEmpty envVar then defaultVal
    else envVar


let host () = "POSTGRES_HOST" |> getEnvOrDefault "localhost"
let user () = "POSTGRES_USER" |> getEnvOrDefault "postgres"
let pass () =  "POSTGRES_PASS"|> getEnvOrDefault "postgres"
let db () = "POSTGRES_DB"|> getEnvOrDefault "postgres"


[<Tests>]
let tests = 
    let connString = 
        sprintf "Host=%s;Username=%s;Password=%s;Database=%s" (host ()) (user ()) (pass()) (db ())
        |> NpgsqlConnectionStringBuilder
    let runMigration = runMigration (Assembly.GetExecutingAssembly())
    testList "Disposable Directory tests" [
        yield! testFixture' (withDisposableDatabase connString runMigration) disposableDirectoryTests 
      ]

[<EntryPoint>]
let main argv =
  runTestsInAssembly defaultConfig argv