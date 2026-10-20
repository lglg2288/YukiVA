using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YukiVA.Orchestrator.Domain.Entities;

namespace YukiVA.Orchestrator.Infrastructure.Persistence.Configurations;

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedOnAdd();

        builder.Property(m => m.PublicId).IsRequired();
        builder.HasIndex(m => m.PublicId).IsUnique();

        builder.Property(m => m.SessionId).IsRequired();

        // Роль — enum. Сохраняем как строку ("User"/"Assistant"...),
        // а не число: в БД читаемо и устойчиво к перестановке значений enum.
        builder.Property(m => m.Role)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(m => m.Text);
        builder.Property(m => m.ToolCallId).HasMaxLength(100);
        builder.Property(m => m.CreatedAt).IsRequired();

        // Вызовы инструментов — value objects, одной jsonb-колонкой в строке сообщения.
        builder.OwnsMany(m => m.ToolCalls, b => b.ToJson("tool_calls"));
        builder.Navigation(m => m.ToolCalls).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
