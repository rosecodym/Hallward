// Copyright © Cody Rose 2016

module Hallward.Uploader.Session

open System
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Security.Cryptography
open System.Text

open Newtonsoft.Json

let private endpointUrl = "https://i.instagram.com/api/v1"
let private instagramVersion = "9.0.1"
let private key = "96724bcbd4fb3e608074e185f2d4f119156fcca061692a4a1db1c7bf142d3e22"
let private deviceId = "android-4bca48fddbe5477d"

let private versions = [| "GT-N7000"; "SM-N9000"; "GT-I9220"; "GT-I9100" |]
let private resolutions = [| "720x1280"; "320x480"; "480x800"; "1024x768"; "1280x720"; "768x1024"; "480x320" |]
let private dpis = [| "120"; "160"; "320"; "240" |]

let private imageCompression =
    [
        "lib_name", "jt"
        "lib_version", "1.3.0"
        "quality", "70"
    ] |> Map.ofSeq

let private hexify (bytes: byte[]) =
    let sb = StringBuilder()
    bytes |> Array.iter (fun b -> sb.Append(b.ToString("x2")) |> ignore)
    sb.ToString()

let private generateUserAgent () =
    let rand = Random()
    
    let androidVersion = sprintf "%i/%i.%i.%i" (rand.Next(10, 11)) (rand.Next(1, 3)) (rand.Next(3, 5)) (rand.Next(0, 5))
    let dpi = dpis.[rand.Next(0, dpis.Length - 1)]
    let resolution = resolutions.[rand.Next(0, resolutions.Length - 1)]
    let version = versions.[rand.Next(0, versions.Length - 1)]

    sprintf "Instagram %s Android (%s; %s; %s; samsung; %s; %s; smdkc210; en_US)"
            instagramVersion
            androidVersion
            dpi
            resolution
            version
            version

let private generateSignature (data: string) =
    let encoding = UTF8Encoding()
    let keyBytes = encoding.GetBytes(key)
    use hmac = new HMACSHA256(keyBytes)
    hmac.Initialize();
    let bytes = hmac.ComputeHash(encoding.GetBytes(data))
    hexify bytes

let inline private success (response: HttpResponseMessage) value =
    { Value = Some(value); Code = response.StatusCode; Reason = response.ReasonPhrase}

let inline private failure (response: HttpResponseMessage) =
    { Value = None; Code = response.StatusCode; Reason = response.ReasonPhrase }

let login username password =
    let uuid = Guid.NewGuid()
    let loginInfo =
        [
            "device_id", deviceId :> obj
            "_uuid", uuid.ToString() :> _
            "username", username :> _
            "password", password :> _
            "_csrftoken", "missing" :> _
            "login_attempt_count", 0 :> _
        ] |> Map.ofSeq
    let loginData = JsonConvert.SerializeObject(loginInfo)
    let escapedData = Uri.EscapeDataString(loginData)
    let signature = generateSignature loginData
    let payload = sprintf "signed_body=%s.%s&ig_sig_key_version=4" signature escapedData
    let encoding = UTF8Encoding()
    let payloadBytes = encoding.GetBytes(payload)

    async {
        let handler = new HttpClientHandler()
        handler.UseCookies <- true
        let httpClient = new HttpClient(handler, disposeHandler=true)
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(generateUserAgent())
        use content = new StringContent(payload, UTF8Encoding())
        content.Headers.ContentType <- MediaTypeHeaderValue("application/x-www-form-urlencoded")
        let endpoint = sprintf "%s/accounts/login/" endpointUrl

        let! response = httpClient.PostAsync(endpoint, content) |> Async.AwaitTask
        match response.IsSuccessStatusCode with
        | true ->
            let client = { HttpClient = httpClient; Uuid = uuid; DeviceId = deviceId }
            return success response client
        | false ->
            let res = failure response
            httpClient.Dispose()
            return res
    }
    
let upload (client: Client) (filename: string) photoStream =
    async {
        let time = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        let data =
            [
                "_csrftoken", "missing"
                "upload_id", time.ToString()
                "device_id", client.DeviceId
                "_uuid", client.Uuid.ToString()
                "image_compression", JsonConvert.SerializeObject(imageCompression)
                "filename", sprintf "pending_media_%i.jpg" time
            ]
        // TODO: Figure out whether MultipartFormDataContent will Dispose these
        let dataContents = data |> List.map (fun (k, v) ->
            let c = new StringContent(v)
            (c, k))

        use content = new MultipartFormDataContent()
        dataContents |> List.iter content.Add
        use photo = new StreamContent(photoStream)
        content.Add(photo, "photo", filename)

        client.HttpClient.DefaultRequestHeaders.Accept.ParseAdd("*/*")
        client.HttpClient.DefaultRequestHeaders.AcceptEncoding.Add(StringWithQualityHeaderValue("gzip"))
        client.HttpClient.DefaultRequestHeaders.AcceptEncoding.Add(StringWithQualityHeaderValue("deflate"))
        client.HttpClient.DefaultRequestHeaders.Connection.Add("keep-alive")
        client.HttpClient.DefaultRequestHeaders.Expect.Clear()

        let endpoint = sprintf "%s/upload/photo/" endpointUrl

        let! response = client.HttpClient.PostAsync(endpoint, content) |> Async.AwaitTask
        match response.IsSuccessStatusCode with
        | true ->
            let! responseBody = response.Content.ReadAsStringAsync() |> Async.AwaitTask
            let json = JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, string>>(responseBody)
            return success response json.["upload_id"]
        | false ->
            return failure response
    }

let configure client uploadId caption =
    let info =
        [
            "device_id", client.DeviceId :> obj
            "_uuid", client.Uuid.ToString() :> _
            "_csrftoken", "missing" :> _
            "media_id", uploadId :> _
            "caption", caption :> _
            "device_timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() :> _
            "source_type", 5 :> _
            "filter_type", 0 :> _
            "extra", "{}" :> _
            "ContentType", "application/x-www-form-urlencoded; charset=UTF-8" :> _
        ] |> Map.ofSeq

    let data = JsonConvert.SerializeObject(info)
    let escapedData = Uri.EscapeDataString(data)
    let signature = generateSignature data

    let payload = sprintf "signed_body=%s.%s&ig_sig_key_version=4" signature escapedData

    let encoding = UTF8Encoding()
    let payloadBytes = encoding.GetBytes(payload)

    async {
        use content = new StringContent(payload, UTF8Encoding())
        content.Headers.ContentType <- MediaTypeHeaderValue("application/x-www-form-urlencoded")
        let endpoint = sprintf "%s/media/configure/" endpointUrl

        let! response = client.HttpClient.PostAsync(endpoint, content) |> Async.AwaitTask
        match response.IsSuccessStatusCode with
        | true ->
            return success response client
        | false ->
            return failure response
    }