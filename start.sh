#!/bin/sh
dotnet restore
screen -S FreneticBot dotnet run $1
