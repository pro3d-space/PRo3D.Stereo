namespace PRo3D.Stereo

open System.IO
open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.Application
open Aardvark.SceneGraph.Opc
open Aardvark.Application.Slim
open Aardvark.GeoSpatial.Opc
open Aardvark.Rendering.Text
open Aardvark.Glfw

open FSharp.Data.Adaptive 
open MBrace.FsPickler

[<AutoOpen>]
module Shader =

    open Aardvark.Base
    open Aardvark.Rendering
    open Aardvark.Rendering.Effects
    
    open FShade

    let LoDColor  (v : Vertex) =
        fragment {
            if uniform?LodVisEnabled then
                let c : V4d = uniform?LoDColor
                let gamma = 1.0
                let grayscale = 0.2126 * v.c.X ** gamma + 0.7152 * v.c.Y ** gamma  + 0.0722 * v.c.Z ** gamma 
                return grayscale * c 
            else return v.c
        }

    let stableTrafo (v : Vertex) =
        vertex {
            let vp = uniform.ModelViewTrafo * v.pos
            let wp = uniform.ModelTrafo * v.pos
            return {
                pos = uniform.ProjTrafo * vp
                wp = wp
                n = uniform.NormalMatrix * v.n
                b = uniform.NormalMatrix * v.b
                t = uniform.NormalMatrix * v.t
                c = v.c
                tc = v.tc
            }
        }


module Trafo = 
    let (^) l r = sprintf "%s%s" l r

    let readLine (filePath:string) =
            use sr = new System.IO.StreamReader (filePath)
            sr.ReadLine ()       

    let fromPath (trafoPath:string) =
         match System.IO.File.Exists trafoPath with
            | true ->
                let t = readLine trafoPath
                Trafo3d.Parse(t)
            | false -> 
                Trafo3d.Identity


module Helpers =

    /// <summary>
    /// checks if "path" is a valid opc folder containing "images" or "Images", "patches" or "Patches", and patchhierarchy.xml
    /// </summary>
    let isOpcFolder' (quiet : bool) (path : string) = 
        let isOpcFolder =
            let imagesProbingPaths = 
                OpcPaths.Images_DirNames |> List.map (fun imageSuffix -> Path.combine [path; imageSuffix])
            let patchesProbingPaths =
                OpcPaths.Patches_DirNames|> List.map (fun patchSuffix -> Path.combine [path; patchSuffix])

            let imagesPath = imagesProbingPaths |> List.tryFind Directory.Exists
            let patchesPath = patchesProbingPaths |> List.tryFind Directory.Exists
            let patchHierarchyXmlPath = 
                patchesPath 
                |> Option.map (fun patchPath -> Path.Combine(patchPath, OpcPaths.PatchHierarchy_FileName))

            imagesPath.IsSome && patchesPath.IsSome && patchHierarchyXmlPath.IsSome

        if not quiet then 
            printfn "[Surface.Files] is opc path: %A => %b" path isOpcFolder

        isOpcFolder

    let isOpcFolder (path : string) = isOpcFolder' true path

module Viewer = 
    
    let getEstimateBoundingBox (p : PatchHierarchy) = 
        match p.tree with
        | QTree.Leaf l -> l.info.GlobalBoundingBox
        | QTree.Node(v, _) -> v.info.GlobalBoundingBox

    let run (surfaceOrOpcPath : string) =

        Aardvark.Init()

        let dbg = Aardvark.Rendering.GL.DebugConfig.Normal
        let windowInterop = StereoExperimental.stereo dbg
        //use app = new OpenGlApplication(dbg, windowInterop, None, false)
        use app = new OpenGlApplication()
        let win = app.CreateGameWindow({ WindowConfig.Default with samples = 8; width = 1760; height= 1080 })
        let runtime = win.Runtime

        let runner = runtime.CreateLoadRunner 1 

        let serializer = FsPickler.CreateBinarySerializer()

        let patchHierarchyPaths = 
            if Helpers.isOpcFolder surfaceOrOpcPath then
                [surfaceOrOpcPath]
            else
                Directory.EnumerateDirectories(surfaceOrOpcPath) |> Seq.toList

        let hierarchies = 
            patchHierarchyPaths |> List.map (fun basePath -> 
                let h = PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths basePath)
                let t = PatchLod.toRoseTree h.tree
                let sg = 
                    Sg.patchLod win.FramebufferSignature runner basePath DefaultMetrics.mars2 false false ViewerModality.XYZ PatchLod.CoordinatesMapping.Local true t

                sg, getEstimateBoundingBox h
            )

        let overallBoundingBox = 
            hierarchies |> List.fold (fun (s : Box3d) (_,b) -> s.Union b) Box3d.Invalid

        let speed = AVal.init 5.0

        let eyeDistance = cval 0.05
        let convergence = cval 1.00


        let bb = overallBoundingBox
        let initialView = CameraView.lookAt bb.Max bb.Center bb.Center.Normalized
        let cameraView = 
            AVal.integrate initialView win.Time [
                DefaultCameraController.controlOrbitAround win.Mouse (AVal.constant bb.Center)
                DefaultCameraController.controlZoom win.Mouse
                DefaultCameraController.controllScroll win.Mouse win.Time
            ]

        let projTrafos = 
            let stereoSettings = AVal.map2 (fun e c -> (e,c)) eyeDistance convergence
            // the frustum needs to depend on the window size (in oder to get proper aspect ratio)
            (win.Sizes, stereoSettings, cameraView) 
                // construct a standard perspective frustum (60 degrees horizontal field of view,
                // near plane 0.1, far plane 50.0 and aspect ratio x/y.
                |||> AVal.map3 (fun windowSize (eyeDistance, convergence) cameraView -> 

                    // this demo shows the basic working.
                    // for further tuning see 
                    // - Schneider Digital has great material online: https://www.3d-pluraview.com/de/technische-daten/support-bereich
                    // also including UI/Cursor and camera considerations.
                    // - https://developer.download.nvidia.com/presentations/2009/SIGGRAPH/3DVision_Develop_Design_Play_in_3D_Stereo.pdf 
                    let separation = eyeDistance * 0.5
                    let camFov = 70.0
                
                    // use this to focus always on orbit (screen = orbit center)
                    let convergence = cameraView.Location.Length

                    let center = Frustum.perspective camFov 0.001 100.0 (float windowSize.X / float windowSize.Y) |> Frustum.projTrafo
                    let p = center.Forward
                    let pl = 
                        M44d(
                            p.M00, p.M01, p.M02 - separation, p.M03-separation*convergence,
                            p.M10, p.M11, p.M12, p.M13,
                            p.M20, p.M21, p.M22, p.M23,
                            p.M30, p.M31, p.M32, p.M33
                        )
                    let pr = 
                        M44d(
                            p.M00, p.M01, p.M02 + separation, p.M03+separation*convergence,
                            p.M10, p.M11, p.M12, p.M13,
                            p.M20, p.M21, p.M22, p.M23,
                            p.M30, p.M31, p.M32, p.M33
                        )

                    [|
                        Trafo3d(pr, pr.Inverse)
                        Trafo3d(pl, pl.Inverse)
                    |]
                )

        let lodVisEnabled = cval true
        let fillMode = cval FillMode.Fill

        win.Keyboard.DownWithRepeats.Values.Add(fun k -> 
            transact (fun _ -> 
                match k, win.Keyboard.IsDown(Keys.LeftAlt).GetValue() with
                | Keys.OemPlus, false -> 
                    eyeDistance.Value <- eyeDistance.Value + 0.001
                | Keys.OemMinus, false ->
                    eyeDistance.Value <- eyeDistance.Value - 0.001
                | Keys.OemPlus, true -> 
                    convergence.Value <- convergence.Value + 0.001
                | Keys.OemMinus, true ->
                    convergence.Value <- convergence.Value - 0.001
                | _ -> ()

                Log.line $"eye distance: {eyeDistance.Value}m, convergence: {convergence.Value}" 
            )
        )

        win.Keyboard.KeyDown(Keys.PageUp).Values.Add(fun _ -> 
            transact (fun _ -> speed.Value <- speed.Value * 1.5)
        )

        win.Keyboard.KeyDown(Keys.PageDown).Values.Add(fun _ -> 
            transact (fun _ -> speed.Value <- speed.Value / 1.5)
        )

        win.Keyboard.KeyDown(Keys.L).Values.Add(fun _ -> 
            transact (fun _ -> lodVisEnabled.Value <- not lodVisEnabled.Value)
        )

        win.Keyboard.KeyDown(Keys.F).Values.Add(fun _ -> 
            transact (fun _ -> 
                fillMode.Value <-
                    match fillMode.Value with
                    | FillMode.Fill -> FillMode.Line
                    | _-> FillMode.Fill
            )
        )

        let helpTextSg =

            let helpText = 
                (eyeDistance, convergence) ||> AVal.map2 (fun eyeDistance convergence -> 
                    String.concat "\r\n" [
                        $"Eye Distance: {eyeDistance}, Screen distance: {convergence}"
                        $""
                        $"Key Bindings:"
                        $"  Plus or Minus        increase/decrease eye separation"
                        //$"  Left Alt+Plus/Minus  increase/decrease screen distance"
                        $"  Mouse Wheel          zoom"
                        $"  Drag Mouse           rotate around object"
                        $""
                    ]
                )

            let trafo = 
                win.Sizes |> AVal.map (fun s -> 
                    let border = V2d(20.0, 10.0) / V2d s
                    let pixels = 30.0 / float s.Y
                    Trafo3d.Scale(pixels) *
                    Trafo3d.Scale(float s.Y / float s.X, 1.0, 1.0) *
                    Trafo3d.Translation(-1.0 + border.X, 1.0 - border.Y - pixels, -1.0)
                )

            let font = FontSquirrel.Hack.Regular
            // Use NoBoundary to resolve issue with render passes, such the Cube not being visible when behind the text in the WriteBuffers example
            let textCfg = TextConfig.create font C4b.White TextAlignment.Left false RenderStyle.NoBoundary
            Sg.textWithConfig textCfg helpText
                |> Sg.trafo trafo
                |> Sg.uniform "ViewTrafo" (Trafo3d.Identity |> Array.create 2 |> AVal.constant)
                |> Sg.uniform "ProjTrafo" (Trafo3d.Identity |> Array.create 2 |> AVal.constant)
                |> Sg.viewTrafo (AVal.constant Trafo3d.Identity)
                |> Sg.projTrafo (AVal.constant Trafo3d.Identity)
   

        let sg = 
            hierarchies
            |> List.map fst
            |> Sg.ofList 
            |> Sg.uniform "ViewTrafo" (cameraView |> AVal.map (CameraView.viewTrafo >> Array.create 2))
            |> Sg.uniform "ProjTrafo" projTrafos
            |> Sg.effect [
                        Shader.stableTrafo |> toEffect
                        DefaultSurfaces.constantColor C4f.White |> toEffect
                        DefaultSurfaces.diffuseTexture |> toEffect
                    ]
            |> Sg.fillMode fillMode
            |> Sg.andAlso helpTextSg

        win.RenderTask <- runtime.CompileRender(win.FramebufferSignature, sg)
        win.Run()
        0