using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace WebApplicationDemo.Data
{
    public class TodoConfiguration : IEntityTypeConfiguration<Todo>
    {
        public void Configure(EntityTypeBuilder<Todo> builder)
        {
            builder.ToTable("Todos");
            builder.HasKey(t => t.Id);
            builder.Property(t => t.Title).IsRequired().HasMaxLength(200);
            builder.Property(t => t.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

            // Données de départ, insérées à la création de la base
            var seedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            builder.HasData(
                new Todo { Id = 1, Title = "Préparer la démo Azure", IsDone = true, CreatedAt = seedDate },
                new Todo { Id = 2, Title = "Créer l'App Service et la base SQL", IsDone = true, CreatedAt = seedDate },
                new Todo { Id = 3, Title = "Déployer depuis Visual Studio", IsDone = false, CreatedAt = seedDate });
        }
    }
}
