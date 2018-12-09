
namespace Tests

module Migrations =
    open SimpleMigrations

    [<Migration(20181212000000L, "Create Animal Table")>]
    type ``Create uuid extension`` () =
        inherit Migration()
            override __.Up () =
                base.Execute
                    """CREATE TABLE animals
                    (
                        animal_id uuid PRIMARY KEY NOT NULL DEFAULT uuid_generate_v4(),
                        name text NOT NULL,
                        birthday timestamp without time zone NOT NULL
                    );"""
            override __.Down () =
                // Not gonna delete it in case other things are using it.
                ()