using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YukiVA.Orchestrator.Domain.Entities;


namespace YukiVA.Orchestrator.Infrastructure.Persistence.Configurations
{
    public class ConversationSessionConfiguration : IEntityTypeConfiguration<ConversationSession>
    {
        public void Configure(EntityTypeBuilder<ConversationSession> builder)
        {
            builder.ToTable("conversation_sessions");
            builder.HasKey(s => s.Id);
            builder.Property(s => s.Id).ValueGeneratedOnAdd();

            builder.Property(s => s.PublicId).IsRequired();
            builder.HasIndex(s => s.PublicId).IsUnique();

            builder.Property(s => s.CreatedAt).IsRequired();

            builder.HasMany(s => s.Messages)
                .WithOne()
                .HasForeignKey(m => m.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Metadata
                .FindNavigation(nameof(ConversationSession.Messages))!.SetPropertyAccessMode(PropertyAccessMode.Field);
        }
    }
}
