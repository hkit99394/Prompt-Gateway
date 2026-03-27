using ControlPlane.Api;

var builder = WebApplication.CreateBuilder(args);

// The repo now supports both ECS and Lambda HTTP hosts. Lambda/API Gateway promotion is in
// progress, while the ECS host remains the explicit rollback path until cutover evidence is complete.
builder.Services.AddControlPlaneHttpApi(builder.Configuration);

var app = builder.Build();
app.MapControlPlaneHttpApi();

app.Run();

public partial class Program
{
}
