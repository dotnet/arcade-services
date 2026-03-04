var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.AddSqlServerClient("BuildInsights");
builder.AddRedisClient("bi-redis");
builder.AddAzureBlobServiceClient("blobs");

var app = builder.Build();

app.MapControllers();

app.Run();
