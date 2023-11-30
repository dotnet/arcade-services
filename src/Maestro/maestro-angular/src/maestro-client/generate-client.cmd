dotnet tool restore

dotnet swaggergen -l angular -i https://maestro.dot.net/api/swagger.json -c Maestro -o %~dp0
