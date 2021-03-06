﻿namespace RProvider

open System
open System.Collections.Generic
open System.Reflection
open System.IO
open System.Diagnostics
open System.Threading
open Microsoft.Win32
open System.IO
open RProviderServer

module internal RInteropClient =

    [<Literal>]
    let server = "RProvider.Server.exe"

    // true to load the server in-process, false load the server out-of-process
    let localServer = false

    let mutable lastServer = None
    let serverlock = "serverlock"
    let GetServer() =
        lock serverlock (fun () ->
            match lastServer with
            | Some s -> s
            | None ->
                match localServer with
                | true -> new RInteropServer()
                | false ->
                    let channelName =
                        let randomSalt = System.Random()
                        let pid = System.Diagnostics.Process.GetCurrentProcess().Id
                        let tick = System.Environment.TickCount
                        let salt = randomSalt.Next()
                        sprintf "RInteropServer_%d_%d_%d" pid tick salt

                    let createdNew = ref false
                    use serverStarted = new EventWaitHandle(false, EventResetMode.ManualReset, channelName, createdNew);
                    assert !createdNew
                    let fsharpCoreName = System.Reflection.AssemblyName("FSharp.Core")
                    let fsharpCoreAssembly =
                        System.AppDomain.CurrentDomain.GetAssemblies()
                        |> Seq.tryFind(
                            fun a-> System.Reflection.AssemblyName.ReferenceMatchesDefinition(fsharpCoreName, a.GetName()))
                            
                    /// The location of the RProvider assembly.
                    /// If the assembly has been shadow-copied, this will be the assembly's
                    /// original location, not the shadow-copied location.
                    let assem = Assembly.GetExecutingAssembly()
                    let assemblyLocation = assem |> RProvider.Internal.Configuration.getAssemblyLocation

                    let exePath = Path.Combine(Path.GetDirectoryName(assemblyLocation), server)
                    let arguments = channelName
                    let startInfo = ProcessStartInfo(UseShellExecute = false, CreateNoWindow = true, FileName=exePath, Arguments = arguments, WindowStyle = ProcessWindowStyle.Hidden)
                    let p = Process.Start(startInfo, EnableRaisingEvents = true)
                    let maxSeconds = 15;
                    let maxTimeSpan = new TimeSpan(0, 0, maxSeconds);
                    let success = serverStarted.WaitOne(maxTimeSpan)
                    if not success then
                        let msg = (sprintf "Failed to start the R.NET server within %d seconds. \
                                           This indicates a loading problem.  You may be able to diagnose \
                                           an issue by running the following command in the console and \
                                           looking for an error message:\n\
                                           RProvider.Server.exe %s" maxSeconds arguments)
                        failwith msg

                    p.Exited.Add(fun _ -> lastServer <- None)
                    let server = Activator.GetObject(typeof<RInteropServer>, "ipc://" + channelName + "/RInteropServer") :?> RInteropServer
                    lastServer <- Some server
                    server
                    )

    let withServer f =
        lock serverlock <| fun () ->
        let server = GetServer()
        f server