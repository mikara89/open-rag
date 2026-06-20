using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace OpenRAG.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MigrateEmbeddingVectorToPgvector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Vector>(
                name: "Vector",
                table: "document_embeddings",
                type: "vector",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "bytea");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte[]>(
                name: "Vector",
                table: "document_embeddings",
                type: "bytea",
                nullable: false,
                oldClrType: typeof(Vector),
                oldType: "vector");
        }
    }
}
