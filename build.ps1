dotnet restore
dotnet build
dotnet publish nuget-mirror -c Release -r win-x64 -o ../publish/win-x64 /p:ShowLinkerSizeComparison=true