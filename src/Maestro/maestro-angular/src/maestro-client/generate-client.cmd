dotnet tool restore

dotnet swaggergen -l angular -i https://maestro-prod.westus2.cloudapp.azure.com/api/swagger.json -c Maestro -o %~dp0
