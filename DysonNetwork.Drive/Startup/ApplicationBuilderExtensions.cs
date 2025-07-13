namespace DysonNetwork.Drive.Startup;

public static class ApplicationBuilderExtensions
{
    public static WebApplication ConfigureAppMiddleware(this WebApplication app, IConfiguration configuration)
    {
        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();
        app.UseAuthorization();
        app.MapControllers();

        return app;
    }

    public static WebApplication ConfigureGrpcServices(this WebApplication app)
    {
        // Map your gRPC services here
        // Example: app.MapGrpcService<MyGrpcService>();

        return app;
    }
}
