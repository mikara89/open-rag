using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenRAG.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MigrateEmbeddingVectorToPgvector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The previous bytea mapping stored each float as four little-endian
            // IEEE-754 bytes. PostgreSQL has no implicit bytea -> vector cast, so
            // a plain AlterColumn fails even on an empty database (SQLSTATE 42804).
            // Convert explicitly so existing embeddings are preserved as well.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION openrag_bytea_to_vector(data bytea, dimensions integer)
                RETURNS vector
                LANGUAGE plpgsql
                IMMUTABLE
                STRICT
                AS $$
                DECLARE
                    values real[] := ARRAY[]::real[];
                    bits bigint;
                    exponent integer;
                    mantissa bigint;
                    sign_value double precision;
                    value double precision;
                    index_value integer;
                BEGIN
                    IF octet_length(data) <> dimensions * 4 THEN
                        RAISE EXCEPTION 'Embedding byte length does not match dimensions';
                    END IF;

                    FOR index_value IN 0..dimensions - 1 LOOP
                        bits := get_byte(data, index_value * 4)::bigint
                            + (get_byte(data, index_value * 4 + 1)::bigint << 8)
                            + (get_byte(data, index_value * 4 + 2)::bigint << 16)
                            + (get_byte(data, index_value * 4 + 3)::bigint << 24);
                        sign_value := CASE WHEN (bits & 2147483648) <> 0 THEN -1.0 ELSE 1.0 END;
                        exponent := ((bits >> 23) & 255)::integer;
                        mantissa := bits & 8388607;

                        IF exponent = 255 THEN
                            RAISE EXCEPTION 'Non-finite embedding values cannot be migrated';
                        ELSIF exponent = 0 THEN
                            value := sign_value * (mantissa::double precision / 8388608.0)
                                * power(2.0, -126);
                        ELSE
                            value := sign_value * (1.0 + mantissa::double precision / 8388608.0)
                                * power(2.0, exponent - 127);
                        END IF;

                        values := array_append(values, value::real);
                    END LOOP;

                    RETURN values::vector;
                END;
                $$;

                ALTER TABLE document_embeddings
                    ALTER COLUMN "Vector" TYPE vector
                    USING openrag_bytea_to_vector("Vector", "EmbeddingDimensions");

                DROP FUNCTION openrag_bytea_to_vector(bytea, integer);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE FUNCTION openrag_vector_to_bytea(data vector, dimensions integer)
                RETURNS bytea
                LANGUAGE plpgsql
                IMMUTABLE
                STRICT
                AS $$
                DECLARE
                    values real[];
                    network_bytes bytea;
                    result bytea := ''::bytea;
                    index_value integer;
                BEGIN
                    values := vector_to_float4(data, dimensions, false);
                    FOR index_value IN 1..dimensions LOOP
                        network_bytes := float4send(values[index_value]);
                        result := result
                            || substring(network_bytes FROM 4 FOR 1)
                            || substring(network_bytes FROM 3 FOR 1)
                            || substring(network_bytes FROM 2 FOR 1)
                            || substring(network_bytes FROM 1 FOR 1);
                    END LOOP;
                    RETURN result;
                END;
                $$;

                ALTER TABLE document_embeddings
                    ALTER COLUMN "Vector" TYPE bytea
                    USING openrag_vector_to_bytea("Vector", "EmbeddingDimensions");

                DROP FUNCTION openrag_vector_to_bytea(vector, integer);
                """);
        }
    }
}
