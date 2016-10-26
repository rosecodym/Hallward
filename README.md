# hallward
A little F# library for uploading images via Instagram's private API

This is basically just a (partial) port of https://github.com/mr0re1/pynstagram. It uses Instagram's private API, which they don't want you to do, so use at your own risk.

## Usage
    open System.IO
    open System.Net
    
    open Hallward.Uploader

    [<EntryPoint>]
    let main argv = 
        async {
            printf "(login) "
            let! loginRes = Session.login "username" "password"
            printf "%i %s\n" (int loginRes.Code) loginRes.Reason
            if loginRes.Value.IsNone then return () else

            let client = loginRes.Value.Value
            use image = new FileStream(@"C:\path\to\whatever.jpg", FileMode.Open)
            printf "(upload) "
            let filename = Path.GetFileName path
            let! uploadRes = Session.upload client "whatever.jpg" image
            printf "%i %s\n" (int uploadRes.Code) uploadRes.Reason
            if uploadRes.Code <> HttpStatusCode.OK then return () else

            printf "(configure) "
            let! configureRes = Session.configure client uploadRes.Value.Value "caption"
            printf "%i %s\n" (int configureRes.Code) configureRes.Reason
        } |> Async.RunSynchronously
        0
