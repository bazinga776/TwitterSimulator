open System
open System.Collections.Generic
open Akka.Actor
open Akka.Configuration
open Akka.FSharp

open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.ServerErrors
open Suave.Writers
open Newtonsoft.Json
open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket

let system = ActorSystem.Create("TwitterEngine")

let setCORSHeaders =
    setHeader  "Access-Control-Allow-Origin" "*"
    >=> setHeader "Access-Control-Allow-Headers" "content-type"

let mutable users = Map.empty
let mutable activeUsers = Map.empty
let mutable tweetOwner = Map.empty
let mutable followers = Map.empty
let mutable mentions = Map.empty
let mutable hashTags = Map.empty
let mutable websockmap = Map.empty


type NewAnswer =
    {
        Text: string
    }
type Answer = 
    {
        Text: string
        AnswerId: int
    }


type RespMsg =
    {
        Comment: string
        Content: list<string>
        status: int
        error: bool
    }
      

type Register =
    {
        UserName: string
        Password: string
    }

type Login =
    {
        UserName: string
        Password: string
    }

type Logout =
    {
        UserName: string
    }

type Follower = 
    {
        UserName: string
        Following: string
    }

type NewTweet =
    {
        Tweet: string
        UserName: string
    }
let buildByteResponseToWS (message:string) =
    message
    |> System.Text.Encoding.ASCII.GetBytes
    |> ByteSegment


let addUser (user: Register) =
    let temp = users.TryFind(user.UserName)
    if temp = None then
        users <- users.Add(user.UserName,user.Password)
        {Comment = "Sucessfully registered! Please login!!";Content=[];status=1;error=false}
    else
        {Comment = "Please Try Again!! The entered user exists!";Content=[];status=1;error=true}

let loginuser (user: Login) = 
    printfn "Received Request to login from %s as %A" user.UserName user
    let temp = users.TryFind(user.UserName)
    if temp = None then
        {Comment = "Please Register! The user mentioned is not registered!";Content=[];status=0;error=true}
    else
        if temp.Value.CompareTo(user.Password) = 0 then
            let temp1 = activeUsers.TryFind(user.UserName)
            if temp1 = None then
                activeUsers <- activeUsers.Add(user.UserName,true)
                {Comment = "You have entered the Twitter Application!!";Content=[];status=2;error=false}
            else
                {Comment = "User logged in";Content=[];status=2;error=true}
        else
            {Comment = "Password is incorrect!";Content=[];status=1;error=true}

let logoutuser (user:Logout) = 
    printfn "Request for logout received from %s as %A" user.UserName user
    let temp = users.TryFind(user.UserName)
    if temp = None then
        {Comment = "Please Register! The user mentioned is not registered!";Content=[];status=0;error=true}
    else
        let temp1 = activeUsers.TryFind(user.UserName)
        if temp1 = None then
            {Comment = "USer has not logged in!";Content=[];status=1;error=true}
        else
            activeUsers <- activeUsers.Remove(user.UserName)
            {Comment = "Logout Sucessful!!";Content=[];status=1;error=false}

let isUserLoggedIn username = 
    let temp = activeUsers.TryFind(username)
    if temp <> None then
        1 
    else
        let temp1 = users.TryFind(username)
        if temp1 = None then
            -1 
        else
            0

let checkUserExsistance username =
    let temp = users.TryFind(username)
    temp <> None



type LiveUserHandlerMsg =
    | SendTweet of WebSocket*NewTweet
    | SendMention of WebSocket* NewTweet
    | SelfTweet of WebSocket * NewTweet
    | Following of WebSocket * string

let liveUserHandler (mailbox:Actor<_>) = 
    let rec loop() = actor{
        let! msg = mailbox.Receive()
        match msg with
        |SelfTweet(ws,tweet)->  let response = "You have tweeted '"+tweet.Tweet+"'"
                                let byteResponse = buildByteResponseToWS response
                                let s =socket{
                                                do! ws.send Text byteResponse true
                                                }
                                Async.StartAsTask s |> ignore
        |SendTweet(ws,tweet)->
                                let response = tweet.UserName+" has tweeted '"+tweet.Tweet+"'"
                                let byteResponse = buildByteResponseToWS response
                                let s =socket{
                                                do! ws.send Text byteResponse true
                                                }
                                Async.StartAsTask s |> ignore
                                
        |SendMention(ws,tweet)->
                                let response = tweet.UserName+" mentioned you in tweet '"+tweet.Tweet+"'"
                                let byteResponse = buildByteResponseToWS response
                                let s =socket{
                                                do! ws.send Text byteResponse true
                                                }
                                Async.StartAsTask s |> ignore
        |Following(ws,msg)->
                                let response = msg
                                let byteResponse = buildByteResponseToWS response
                                let s =socket{
                                                do! ws.send Text byteResponse true
                                                }
                                Async.StartAsTask s |> ignore
        return! loop()
    }
    loop()
    
let liveUserHandlerRef = spawn system "luref" liveUserHandler

let tweetParser (tweet:NewTweet) =
    let splits = (tweet.Tweet.Split ' ')
    for i in splits do
        if i.StartsWith "@" then
            let temp = i.Split '@'
            if checkUserExsistance temp.[1] then
                let temp1 = mentions.TryFind(temp.[1])
                if temp1 = None then
                    let mutable mp = Map.empty
                    let tlist = new List<string>()
                    tlist.Add(tweet.Tweet)
                    mp <- mp.Add(tweet.UserName,tlist)
                    mentions <- mentions.Add(temp.[1],mp)
                else
                    let temp2 = temp1.Value.TryFind(tweet.UserName)
                    if temp2 = None then
                        let tlist = new List<string>()
                        tlist.Add(tweet.Tweet)
                        let mutable mp = temp1.Value
                        mp <- mp.Add(tweet.UserName,tlist)
                        mentions <- mentions.Add(temp.[1],mp)
                    else
                        temp2.Value.Add(tweet.Tweet)
                let temp3 = websockmap.TryFind(temp.[1])
                if temp3<>None then
                    liveUserHandlerRef <! SendMention(temp3.Value,tweet)
        elif i.StartsWith "#" then
            let temp1 = i.Split '#'
            let temp = hashTags.TryFind(temp1.[1])
            if temp = None then
                let lst = List<string>()
                lst.Add(tweet.Tweet)
                hashTags <- hashTags.Add(temp1.[1],lst)
            else
                temp.Value.Add(tweet.Tweet)


let addFollower (follower: Follower) =
    printfn "Request for subscribe received from %s as %A" follower.UserName follower
    let status = isUserLoggedIn follower.UserName
    if status = 1 then
        if (checkUserExsistance follower.Following) then
            let temp = followers.TryFind(follower.Following)
            let temp1 = websockmap.TryFind(follower.UserName)
            if temp = None then
                let lst = new List<string>()
                lst.Add(follower.UserName)
                followers <- followers.Add(follower.Following,lst)
                if temp1 <> None then
                    liveUserHandlerRef <! Following(temp1.Value,"You have sucessfully been added as a subscriber to "+follower.Following)
                {Comment = "Subscriber list is updated!";Content=[];status=2;error=false}
            else
                if temp.Value.Exists( fun x -> x.CompareTo(follower.UserName) = 0 ) then
                    if temp1 <> None then
                        liveUserHandlerRef <! Following(temp1.Value,"You already been subscribed to: "+follower.Following)
                    {Comment = "You already been subscribed to: "+follower.Following;Content=[];status=2;error=true}
                else
                    temp.Value.Add(follower.UserName)
                    if temp1 <> None then
                        liveUserHandlerRef <! Following(temp1.Value,"You are now following: "+follower.Following)
                    {Comment = "Subscriber list is updated!";Content=[];status=2;error=false}
        else
            {Comment = "Subscriber "+follower.Following+" doesn't exist";Content=[];status=2;error=true}
    elif status = 0 then
        {Comment = "Login to the application!!";Content=[];status=1;error=true}
    else
        {Comment = "Please Register!! Couldn't find User!";Content=[];status=0;error=true}

let addTweet (tweet: NewTweet) =
    let temp = tweetOwner.TryFind(tweet.UserName)
    if temp = None then
        let lst = new List<string>()
        lst.Add(tweet.Tweet)
        tweetOwner <- tweetOwner.Add(tweet.UserName,lst)
    else
        temp.Value.Add(tweet.Tweet)
    

let addTweetToFollowers (tweet: NewTweet) = 
    let temp = followers.TryFind(tweet.UserName)
    if temp <> None then
        for i in temp.Value do
            let temp1 = {Tweet=tweet.Tweet;UserName=i}
            addTweet temp1
            let temp2 = websockmap.TryFind(i)
            printfn "%s" i
            if temp2 <> None then
                liveUserHandlerRef <! SendTweet(temp2.Value,tweet)

type tweetHandlerMsg =
    | AddTweetMsg of NewTweet
    | AddTweetToFollowersMsg of NewTweet
    | TweetParserMsg of NewTweet


let tweetHandler (mailbox:Actor<_>) =
    let rec loop() = actor{
        let! msg = mailbox.Receive()
        match msg with 
        | AddTweetMsg(tweet) -> addTweet(tweet)
                                let temp = websockmap.TryFind(tweet.UserName)
                                if temp <> None then
                                    liveUserHandlerRef <! SelfTweet(temp.Value,tweet)
        | AddTweetToFollowersMsg(tweet) ->  addTweetToFollowers(tweet)
        | TweetParserMsg(tweet) -> tweetParser(tweet)
        return! loop()
    }
    loop()

let tweetHandlerRef = spawn system "thref" tweetHandler

let addTweetToUser (tweet: NewTweet) =
    let status = isUserLoggedIn tweet.UserName
    if status = 1 then
        tweetHandlerRef <! AddTweetMsg(tweet)
        // addTweet tweet
        tweetHandlerRef <! AddTweetToFollowersMsg(tweet)
        // addTweetToFollowers tweet
        tweetHandlerRef <! TweetParserMsg(tweet)
        {Comment = "Sent tweet sucessfully!!";Content=[];status=2;error=false}
    elif status = 0 then
        {Comment = "Please Login";Content=[];status=1;error=true}
    else
        {Comment = "Please Register!! Couldn't find User!";Content=[];status=0;error=true}

let getTweets username =
    let status = isUserLoggedIn username
    if status = 1 then
        let temp = tweetOwner.TryFind(username)
        if temp = None then
            {Comment = "Tweet history empty! Please go Head and tweet!!";Content=[];status=2;error=false}
        else
            let len = Math.Min(10,temp.Value.Count)
            let res = [for i in 1 .. len do yield(temp.Value.[i-1])] 
            {Comment = "User tweets task sucessfully executed!!";Content=res;status=2;error=false}
    elif status = 0 then
        {Comment = "Login to the application!!";Content=[];status=1;error=true}
    else
        {Comment = "Please Register!! Couldn't find User!";Content=[];status=0;error=true}

let getMentions username = 
    let status = isUserLoggedIn username
    if status = 1 then
        let temp = mentions.TryFind(username)
        if temp = None then
            {Comment = "No Mentions yet!!";Content=[];status=2;error=false}
        else
            let res = new List<string>()
            for i in temp.Value do
                for j in i.Value do
                    res.Add(j)
            let len = Math.Min(10,res.Count)
            let res1 = [for i in 1 .. len do yield(res.[i-1])] 
            {Comment = "Get Mentions done Succesfully";Content=res1;status=2;error=false}
    elif status = 0 then
        {Comment = "Please Login";Content=[];status=1;error=true}
    else
        {Comment = "User Doesn't Exsist!!Please Register";Content=[];status=0;error=true}

let getHashTags username hashtag =
    let status = isUserLoggedIn username
    if status = 1 then
        printf "%s" hashtag
        let temp = hashTags.TryFind(hashtag)
        if temp = None then
            {Comment = "No Tweets with this hashtag found";Content=[];status=2;error=false}
        else
            let len = Math.Min(10,temp.Value.Count)
            let res = [for i in 1 .. len do yield(temp.Value.[i-1])] 
            {Comment = "Get Hashtags done Succesfully";Content=res;status=2;error=false}
    elif status = 0 then
        {Comment = "Please Login";Content=[];status=1;error=true}
    else
        {Comment = "Please Register!! Couldn't find User!";Content=[];status=0;error=true}

let registerNewUser (user: Register) =
    printfn "Request to register from %s as %A" user.UserName user
    addUser user

let respTweet (tweet: NewTweet) =
    printfn "Sending tweet request received from %s as %A" tweet.UserName tweet
    addTweetToUser tweet

let getString (rawForm: byte[]) =
    System.Text.Encoding.UTF8.GetString(rawForm)

let fromJson<'a> json =
    JsonConvert.DeserializeObject(json, typeof<'a>) :?> 'a


let gettweets username =
    printfn "Received GetTweets Request from %s " username
    getTweets username
    |> JsonConvert.SerializeObject
    |> OK
    >=> setMimeType "application/json"
    >=> setCORSHeaders

let getmentions username =
    printfn "Received GetMentions Request from %s " username
    getMentions username
    |> JsonConvert.SerializeObject
    |> OK
    >=> setMimeType "application/json"
    >=> setCORSHeaders

let gethashtags username hashtag =
    printfn "Received GetHashTag Request from %s for hashtag %A" username hashtag
    getHashTags username hashtag
    |> JsonConvert.SerializeObject
    |> OK
    >=> setMimeType "application/json"
    >=> setCORSHeaders

let register =
    request (fun r ->
    r.rawForm
    |> getString
    |> fromJson<Register>
    |> registerNewUser
    |> JsonConvert.SerializeObject
    |> OK
    )
    >=> setMimeType "application/json"
    >=> setCORSHeaders

let login =
    request (fun r ->
    r.rawForm
    |> getString
    |> fromJson<Login>
    |> loginuser
    |> JsonConvert.SerializeObject
    |> OK
    )
    >=> setMimeType "application/json"
    >=> setCORSHeaders

let logout =
    request (fun r ->
    r.rawForm
    |> getString
    |> fromJson<Logout>
    |> logoutuser
    |> JsonConvert.SerializeObject
    |> OK
    )
    >=> setMimeType "application/json"
    >=> setCORSHeaders

let newTweet = 
    request (fun r ->
    r.rawForm
    |> getString
    |> fromJson<NewTweet>
    |> respTweet
    |> JsonConvert.SerializeObject
    |> OK
    )
    >=> setMimeType "application/json"
    >=> setCORSHeaders

let follow =
    request (fun r ->
    r.rawForm
    |> getString
    |> fromJson<Follower>
    |> addFollower
    |> JsonConvert.SerializeObject
    |> OK
    )
    >=> setMimeType "application/json"
    >=> setCORSHeaders




let websocketHandler (webSocket : WebSocket) (context: HttpContext) =
    socket {
        let mutable loop = true

        while loop do
              let! msg = webSocket.read()

              match msg with
              | (Text, data, true) ->
                let str = UTF8.toString data 
                if str.StartsWith("UserName:") then
                    let uname = str.Split(':').[1]
                    websockmap <- websockmap.Add(uname,webSocket)
                    printfn "connected to %s websocket" uname
                else
                    let response = sprintf "response to %s" str
                    let byteResponse = buildByteResponseToWS response
                    do! webSocket.send Text byteResponse true

              | (Close, _, _) ->
                let emptyResponse = [||] |> ByteSegment
                do! webSocket.send Close emptyResponse true
                loop <- false
              | _ -> ()
    }


let allow_cors : WebPart =
    choose [
        OPTIONS >=>
            fun context ->
                context |> (
                    setCORSHeaders
                    >=> OK "CORS approved" )
    ]

let app =
    choose
        [ 
            path "/websocket" >=> handShake websocketHandler 
            allow_cors
            GET >=> choose
                [ 
                path "/" >=> OK "Twitter Server Ok! Please start the twitter.html" 
                pathScan "/gettweets/%s" (fun username -> (gettweets username))
                pathScan "/getmentions/%s" (fun username -> (getmentions username))
                pathScan "/gethashtags/%s/%s" (fun (username,hashtag) -> (gethashtags username hashtag))
                ]

            POST >=> choose
                [   
                path "/newtweet" >=> newTweet 
                path "/register" >=> register
                path "/login" >=> login
                path "/logout" >=> logout
                path "/follow" >=> follow
              ]

            PUT >=> choose
                [ ]

            DELETE >=> choose
                [ ]
        ]

[<EntryPoint>]
let main argv =
    startWebServer defaultConfig app
    0
