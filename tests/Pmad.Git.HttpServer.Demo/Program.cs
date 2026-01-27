using Pmad.Git.HttpServer;

namespace Pmad.Git.HttpServer.Demo
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddRazorPages();

            // Add Git Smart HTTP service (recommended - uses DI)
            builder.Services.AddGitSmartHttp(options =>
            {
                options.RepositoryRoot = Path.Combine(builder.Environment.ContentRootPath, "Repositories");
                options.EnableUploadPack = true;
                options.EnableReceivePack = true; // Enable push for demo purposes
                options.Agent = "Pmad.Git.HttpServer.Demo/1.0";

                // Allow both read and write operations for demo purposes
                // In production, you should check authentication/authorization
                options.AuthorizeAsync = (context, repositoryName, operation, cancellationToken) =>
                {
                    // For demo: allow all operations
                    // In production: return operation == GitOperation.Read || context.User.Identity?.IsAuthenticated == true;
                    return ValueTask.FromResult(true);
                };

                // Optional: Get notified when a push operation completes
                options.OnReceivePackCompleted = async (context, repositoryName, updatedReferences) =>
                {
                    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
                    logger.LogInformation(
                        "Push completed for repository '{Repository}'. Updated {Count} reference(s): {References}",
                        repositoryName,
                        updatedReferences.Count,
                        string.Join(", ", updatedReferences));

                    // Here you could:
                    // - Invalidate application caches
                    // - Trigger webhooks
                    // - Update a database
                    // - Send notifications
                    await Task.CompletedTask;
                };
            });

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.MapRazorPages();

            // Map Git Smart HTTP endpoints (uses service from DI)
            app.MapGitSmartHttp();

            app.Run();
        }
    }
}

