
open System;

[<CLIMutable>]
type TestInput = { Id : int; Value: string }

[<CLIMutable>]
type SampleEntity = { Id: int; Text: string }

let Run(input: TestInput , entity: SampleEntity , log: TraceWriter ) = 
    if (entity.Id <> input.Id) then
        invalidOp ("Expected Id to be bound.")

    entity.Text <- input.Value;

