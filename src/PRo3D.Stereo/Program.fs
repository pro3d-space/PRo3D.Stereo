open PRo3D.Stereo

open CommandLine
open CommandLine.Text

type Arguments = 
    {
        [<Value(0, MetaName="SurfacePath", 
                 HelpText = """Path to the OPC dataset. 
                 Either to a single OPC hierarchy (a folder containig images, patches) 
                 or to a directory containung multiple OPC hierarchies aka surface """)>] 
        surfaceOrOpcDirectory : string
     }

[<EntryPoint>]
let main argv = 
    let result = CommandLine.Parser.Default.ParseArguments<Arguments>(argv)
    match result with
    | :? Parsed<Arguments> as parsed -> 
            Viewer.run parsed.Value.surfaceOrOpcDirectory
    | :? NotParsed<Arguments> as notParsed -> failwithf "could not parse arguments: %A" notParsed.Errors
    | _ -> failwithf "%A" result