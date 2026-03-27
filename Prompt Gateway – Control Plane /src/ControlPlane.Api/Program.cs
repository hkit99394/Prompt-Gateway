using ControlPlane.Api;

var builder = WebApplication.CreateBuilder(args);

// The HTTP control plane is still ECS-hosted today, but the next migration phases target a
// Lambda-hosted HTTP edge after the queue-processing path is fully proven.
builder.Services.AddControlPlaneHttpApi(builder.Configuration);

var app = builder.Build();
app.MapControlPlaneHttpApi();

app.Run();

public partial class Program
{
}
