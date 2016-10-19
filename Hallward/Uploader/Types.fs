// Copyright © Cody Rose 2016

[<AutoOpen>]
module Hallward.Uploader.Types

open System
open System.Net
open System.Net.Http

type Result<'a> = {
    Value: 'a option
    Code: HttpStatusCode
    Reason: string
    }

type Client = {
    DeviceId: string
    HttpClient: HttpClient
    Uuid: Guid
    }
    with
        interface IDisposable with
            member this.Dispose () = this.HttpClient.Dispose()