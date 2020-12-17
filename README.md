# Proj4

## Project Teammates
* Venkata Sindhu Kandula 1914 5414
* Vriddhi Pai 1664 1323

## Steps to run the project
* Unzip the files to a folder
* Go to the path where fsproj file is present
* Run the below commands for running the server
* dotnet restore
* dotnet build 
* dotnet run
* The server is run on localhost:8080
* Now for the frontend, open twitter.html file in web browser

For the functionalities implemented and code explanation, please see the below link
Video Explanation Link: https://youtu.be/CtXOuetXROo

## Code Explanation
* We have two parts for the twitter application - Client.html and Program.fs
* We have used suave.io for implementing the register, login, subscribe requests which are implemnted as endpoints for the POST requests
* To query the tweets, see where the user is mentioned, also for returing the tweets with hashtags, we have used JSON GET requests
* We have used various map data structures for storing the data entered by user.
* The websocket connection is established by the twitter.html and the actor model, which we have implemeted for simulating the live users, we have changed to add the different functionalities for the twitter server and having the websocket to communicate with the client

