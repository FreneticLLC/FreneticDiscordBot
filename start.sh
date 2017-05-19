#!/bin/sh
dotnet restore
screen -dmS FreneticBot dotnet run $1
