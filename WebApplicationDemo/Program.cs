using Microsoft.EntityFrameworkCore;
using WebApplicationDemo.Data;

namespace WebApplicationDemo
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // 1. Déclarer la policy
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("FrontendPolicy", policy =>
                {
                    policy.WithOrigins("https://azure-front-demo-six.vercel.app")    // front déployé sur Azure
                          .AllowAnyHeader()
                          .AllowAnyMethod();
                    // .AllowCredentials(); // seulement si cookies/auth, et jamais avec AllowAnyOrigin
                });
            });

            // Add services to the container.

            builder.Services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(
                    builder.Configuration.GetConnectionString("DefaultConnection"),
                    sql => sql.EnableRetryOnFailure())); // Azure SQL peut être lent à répondre (réveil, failover)

            builder.Services.AddControllers();
            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddOpenApi();

            var app = builder.Build();

            // Code first : applique les migrations en attente au démarrage (crée la base et les tables si besoin)
            using (var scope = app.Services.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
            }

            // 2. Activer le middleware, après UseRouting et avant UseAuthentication/UseAuthorization
            app.UseCors("FrontendPolicy");

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
