az containerapp update --name build-insights-stage --resource-group build-insights-stage-rg `
    --image buildinsightsstage.azurecr.io/build-insights-api:202603043-1-630a680517-dev `
    --command "/bin/sh, -c, cd /app/BuildInsights.DummyApp && dotnet BuildInsights.DummyApp.dll"
