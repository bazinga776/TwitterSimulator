open System
open System.Collections.Generic
open Akka.Actor
open Akka.FSharp
open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.Writers
open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket
open Newtonsoft.Json

//Message Type Declarations
type MessageType =
    {
        Text: string
    }

type MessageType1 = 
    {
        Text: string
        AnswerId: int
    }

type MessageType3 =
    {
        Comment: string
        Content: list<string>
        status: int
        error: bool
    }
      
type LoginDataType =
    {
        uname: string
        pass: string
    }

type Login =
    {
        uname: string
        pass: string
    }

type Logout =
    {
        uname: string
    }

type Follower = 
    {
        uname: string
        Following: string
    }

type NewTweet =
    {
        Tweet: string
        uname: string
    }

type userFeed =
    | NewPost of WebSocket*NewTweet
    | SendMention of WebSocket* NewTweet
    | SelfTweet of WebSocket * NewTweet
    | Following of WebSocket * string

type tweetMessages =
    | AddTweetMsg of NewTweet
    | AddTweetToFollowersMsg of NewTweet
    | TweetParserMsg of NewTweet


let buildByteResponseToWS (message:string) =
    message
    |> System.Text.Encoding.ASCII.GetBytes
    |> ByteSegment

let system = ActorSystem.Create("TwitterServer")
let mutable userList = Map.empty

let setCORSHeaders =
    setHeader  "Access-Control-Allow-Origin" "*"
    >=> setHeader "Access-Control-Allow-Headers" "content-type"

//Twitter Functionalities
//1. Register a new user 
let newRegister (user: LoginDataType) =
    let flag = userList.TryFind(user.uname)
    if flag = None then
        userList <- userList.Add(user.uname,user.pass)
        {Comment = "Welcome New User!";Content=[];status=1;error=false}
    else
        {Comment = "Existing User";Content=[];status=1;error=true}
        

//2. Login functionality for returning users 

let mutable currentUsers = Map.empty
let existingUserLogin (user: Login) = 
    printfn "Received Login Request from %s as %A" user.uname user
    let flag = userList.TryFind(user.uname)
    if flag = None then
        {Comment = "Invalid User! Signup Required! ";Content=[];status=0;error=true}
    else
        if flag.Value.CompareTo(user.pass) = 0 then
            let temp1 = currentUsers.TryFind(user.uname)
            if temp1 = None then
                currentUsers <- currentUsers.Add(user.uname,true)
                {Comment = "Welcome Again!";Content=[];status=2;error=false}
            else
                {Comment = "You're already logged in";Content=[];status=2;error=true}
        else
            {Comment = "Invalid pass! Try again!";Content=[];status=1;error=true}

//3. Check if the user trying to login already exsists or not 
let validUserOrNot username =
    let flag = userList.TryFind(username)
    flag <> None

//4. Checks if the user's session is active or not 

let checkActivity username = 
    let flag = currentUsers.TryFind(username)
    if flag <> None then
        1 // Existing Logged In User
    else
        let temp1 = userList.TryFind(username)
        if temp1 = None then
            -1 // Invalid User 
        else
            0 // Inactive Existing User

//5. Handle Logging the User Out
let userLoggingOut (user:Logout) = 
    printfn "Logging out %s as %A" user.uname user
    let flag = userList.TryFind(user.uname)
    if flag = None then
        {Comment = "Oops! Invalid User! Signup required. ";Content=[];status=0;error=true}
    else
        let temp1 = currentUsers.TryFind(user.uname)
        if temp1 = None then
            {Comment = "User Session Inactive";Content=[];status=1;error=true}
        else
            currentUsers <- currentUsers.Remove(user.uname)
            {Comment = "Logging out!";Content=[];status=1;error=false}


let activeUserActivity (mailbox:Actor<_>) = 
    let rec loop() = actor{
        let! msg = mailbox.Receive()
        match msg with
        |SelfTweet(ws,tweet)->  let response = "Tweet posted!'"+tweet.Tweet+"'"
                                let byteResponse = buildByteResponseToWS response
                                // printfn "Sending data to %A" ws 
                                let s =socket{
                                                do! ws.send Text byteResponse true
                                                }
                                Async.StartAsTask s |> ignore
        |NewPost(ws,tweet)->
                                let response = tweet.uname+" just posted '"+tweet.Tweet+"'"
                                let byteResponse = buildByteResponseToWS response
                                // printfn "Sending data to %A" ws 
                                let s =socket{
                                                do! ws.send Text byteResponse true
                                                }
                                Async.StartAsTask s |> ignore
                                // printfn "%A" err
        |SendMention(ws,tweet)->
                                let response = tweet.uname+" just mentioned you in '"+tweet.Tweet+"'"
                                let byteResponse = buildByteResponseToWS response
                                // printfn "Sending data to %A" ws 
                                let s =socket{
                                                do! ws.send Text byteResponse true
                                                }
                                Async.StartAsTask s |> ignore
        |Following(ws,msg)->
                                let response = msg
                                let byteResponse = buildByteResponseToWS response
                                // printfn "Sending data to %A" ws 
                                let s =socket{
                                                do! ws.send Text byteResponse true
                                                }
                                Async.StartAsTask s |> ignore
        return! loop()
    }
    loop()
    
let liveUserHandlerRef = spawn system "actorForUserActivity" activeUserActivity
let mutable queryHashTags = Map.empty
let mutable wscollection = Map.empty
let mutable userMentions = Map.empty
let tweetParser (tweet:NewTweet) =
    let splits = (tweet.Tweet.Split ' ')
    for i in splits do
        if i.StartsWith "@" then
            let flag = i.Split '@'
            if validUserOrNot flag.[1] then
                let temp1 = userMentions.TryFind(flag.[1])
                if temp1 = None then
                    let mutable mp = Map.empty
                    let tlist = new List<string>()
                    tlist.Add(tweet.Tweet)
                    mp <- mp.Add(tweet.uname,tlist)
                    userMentions <- userMentions.Add(flag.[1],mp)
                else
                    let temp2 = temp1.Value.TryFind(tweet.uname)
                    if temp2 = None then
                        let tlist = new List<string>()
                        tlist.Add(tweet.Tweet)
                        let mutable mp = temp1.Value
                        mp <- mp.Add(tweet.uname,tlist)
                        userMentions <- userMentions.Add(flag.[1],mp)
                    else
                        temp2.Value.Add(tweet.Tweet)
                let temp3 = wscollection.TryFind(flag.[1])
                if temp3<>None then
                    liveUserHandlerRef <! SendMention(temp3.Value,tweet)
        elif i.StartsWith "#" then
            let temp1 = i.Split '#'
            let flag = queryHashTags.TryFind(temp1.[1])
            if flag = None then
                let lst = List<string>()
                lst.Add(tweet.Tweet)
                queryHashTags <- queryHashTags.Add(temp1.[1],lst)
            else
                flag.Value.Add(tweet.Tweet)


let mutable userFollowers = Map.empty
let addFollower (follower: Follower) =
    printfn "Follow Request By %s as %A" follower.uname follower
    let status = checkActivity follower.uname
    if status = 1 then
        if (validUserOrNot follower.Following) then
            let flag = userFollowers.TryFind(follower.Following)
            let temp1 = wscollection.TryFind(follower.uname)
            if flag = None then
                let lst = new List<string>()
                lst.Add(follower.uname)
                userFollowers <- userFollowers.Add(follower.Following,lst)
                if temp1 <> None then
                    liveUserHandlerRef <! Following(temp1.Value,"New Follower: "+follower.Following)
                {Comment = "Added to Followers!";Content=[];status=2;error=false}
            else
                if flag.Value.Exists( fun x -> x.CompareTo(follower.uname) = 0 ) then
                    if temp1 <> None then
                        liveUserHandlerRef <! Following(temp1.Value,"Existing Follower: "+follower.Following)
                    {Comment = "Existing Follower"+follower.Following;Content=[];status=2;error=true}
                else
                    flag.Value.Add(follower.uname)
                    if temp1 <> None then
                        liveUserHandlerRef <! Following(temp1.Value,"New follower: "+follower.Following)
                    {Comment = "Added to Followers!";Content=[];status=2;error=false}
        else
            {Comment = "The follower "+follower.Following+"doesn't exist";Content=[];status=2;error=true}
    elif status = 0 then
        {Comment = "Login Needed!";Content=[];status=1;error=true}
    else
        {Comment = "No such user! Signup!";Content=[];status=0;error=true}


let mutable userTweet = Map.empty
let addTweet (tweet: NewTweet) =
    let flag = userTweet.TryFind(tweet.uname)
    if flag = None then
        let lst = new List<string>()
        lst.Add(tweet.Tweet)
        userTweet <- userTweet.Add(tweet.uname,lst)
    else
        flag.Value.Add(tweet.Tweet)
    

let addTweetToFollowers (tweet: NewTweet) = 
    let flag = userFollowers.TryFind(tweet.uname)
    if flag <> None then
        for i in flag.Value do
            let temp1 = {Tweet=tweet.Tweet;uname=i}
            addTweet temp1
            let temp2 = wscollection.TryFind(i)
            printfn "%s" i
            if temp2 <> None then
                liveUserHandlerRef <! NewPost(temp2.Value,tweet)

let tweetHandler (mailbox:Actor<_>) =
    let rec loop() = actor{
        let! msg = mailbox.Receive()
        match msg with 
        | AddTweetMsg(tweet) -> addTweet(tweet)
                                let flag = wscollection.TryFind(tweet.uname)
                                if flag <> None then
                                    liveUserHandlerRef <! SelfTweet(flag.Value,tweet)
        | AddTweetToFollowersMsg(tweet) ->  addTweetToFollowers(tweet)
        | TweetParserMsg(tweet) -> tweetParser(tweet)
        return! loop()
    }
    loop()

let tweetHandlerRef = spawn system "actorForTweets" tweetHandler
let addTweetToUser (tweet: NewTweet) =
    let status = checkActivity tweet.uname
    if status = 1 then
        tweetHandlerRef <! AddTweetMsg(tweet)
        // addTweet tweet
        tweetHandlerRef <! AddTweetToFollowersMsg(tweet)
        // addTweetToFollowers tweet
        tweetHandlerRef <! TweetParserMsg(tweet)
        // tweetParser tweet
        {Comment = "Posted to Twitter!";Content=[];status=2;error=false}
    elif status = 0 then
        {Comment = "Login Needed!";Content=[];status=1;error=true}
    else
        {Comment = "Invalid User! Signup Required! ";Content=[];status=0;error=true}

let getTweets username =
    let status = checkActivity username
    if status = 1 then
        let flag = userTweet.TryFind(username)
        if flag = None then
            {Comment = "No Tweets";Content=[];status=2;error=false}
        else
            let len = Math.Min(10,flag.Value.Count)
            let res = [for i in 1 .. len do yield(flag.Value.[i-1])] 
            {Comment = "Get Tweets done Succesfully";Content=res;status=2;error=false}
    elif status = 0 then
        {Comment = "Login Needed!";Content=[];status=1;error=true}
    else
        {Comment = "Invalid User! Singup Required!";Content=[];status=0;error=true}

let getMentions username = 
    let status = checkActivity username
    if status = 1 then
        let flag = userMentions.TryFind(username)
        if flag = None then
            {Comment = "No Mentions";Content=[];status=2;error=false}
        else
            let res = new List<string>()
            for i in flag.Value do
                for j in i.Value do
                    res.Add(j)
            let len = Math.Min(10,res.Count)
            let res1 = [for i in 1 .. len do yield(res.[i-1])] 
            {Comment = "Get Mentions done Succesfully";Content=res1;status=2;error=false}
    elif status = 0 then
        {Comment = "Login Needed!";Content=[];status=1;error=true}
    else
        {Comment = "Invalid User! Signup Required!";Content=[];status=0;error=true}

let getHashTags username hashtag =
    let status = checkActivity username
    if status = 1 then
        printf "%s" hashtag
        let flag = queryHashTags.TryFind(hashtag)
        if flag = None then
            {Comment = "No topic matches your search!";Content=[];status=2;error=false}
        else
            let len = Math.Min(10,flag.Value.Count)
            let res = [for i in 1 .. len do yield(flag.Value.[i-1])] 
            {Comment = "Search Complete!";Content=res;status=2;error=false}
    elif status = 0 then
        {Comment = "Login Needed!";Content=[];status=1;error=true}
    else
        {Comment = "Invalid User! Signup Required!";Content=[];status=0;error=true}

let registerNewUser (user: LoginDataType) =
    printfn "Registering %s as %A" user.uname user
    newRegister user

let respTweet (tweet: NewTweet) =
    printfn "Registering %s as %A" tweet.uname tweet
    addTweetToUser tweet

let getString (rawForm: byte[]) =
    System.Text.Encoding.UTF8.GetString(rawForm)

let fromJson<'a> json =
    JsonConvert.DeserializeObject(json, typeof<'a>) :?> 'a

let gettweets username =
    printfn "Topic Searched By %s " username
    getTweets username
    |> JsonConvert.SerializeObject
    |> OK
    >=> setMimeType "application/json"
    >=> setCORSHeaders

let getmentions username =
    printfn "Mention By %s " username
    getMentions username
    |> JsonConvert.SerializeObject
    |> OK
    >=> setMimeType "application/json"
    >=> setCORSHeaders

let gethashtags username hashtag =
    printfn "HashTag By %s for hashtag %A" username hashtag
    getHashTags username hashtag
    |> JsonConvert.SerializeObject
    |> OK
    >=> setMimeType "application/json"
    >=> setCORSHeaders

let register =
    request (fun r ->
    r.rawForm
    |> getString
    |> fromJson<LoginDataType>
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
    |> existingUserLogin
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
    |> userLoggingOut
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

//websocket logic 
let websocketHandler (webSocket : WebSocket) (context: HttpContext) =
    socket {
        let mutable loop = true

        while loop do
              let! msg = webSocket.read()

              match msg with
              | (Text, data, true) ->
                let str = UTF8.toString data 
                if str.StartsWith("uname:") then
                    let uname = str.Split(':').[1]
                    wscollection <- wscollection.Add(uname,webSocket)
                    printfn " %s Connected!" uname  //every client has a separate socket it connects through to activate its session 
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


//website logic 

let app =
    choose
        [ 
            path "/websocket" >=> handShake websocketHandler 
            allow_cors
            GET >=> choose
                [ 
                path "/" >=> OK "Twitter Engine" 
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
