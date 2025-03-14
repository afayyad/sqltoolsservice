//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.SqlTools.ServiceLayer.SchemaDesigner
{
    /// <summary>
    /// Provides methods for generating SQL scripts for database schema creation.
    /// </summary>
    public static class SchemaCreationScriptGenerator
    {

        /// <summary>
        /// Generates CREATE TABLE scripts for all tables in the schema model.
        /// </summary>
        /// <param name="model">The schema model containing tables to script.</param>
        /// <returns>A string containing SQL scripts for creating all tables.</returns>
        public static string GenerateCreateTableScript(SchemaDesignerModel model)
        {
            StringBuilder sb = new StringBuilder();

            foreach (var table in model.Tables)
            {
                sb.AppendLine(GenerateTableDefinition(table));
            }

            sb.AppendLine(GenerateForeignKeyScripts(model.Tables));

            return sb.ToString();
        }

        /// <summary>
        /// Generates a CREATE TABLE script for a single table.
        /// </summary>
        /// <param name="table">The table to script.</param>
        /// <returns>A string containing SQL script for creating the table.</returns>
        public static string GenerateTableDefinition(SchemaDesignerTable table)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"CREATE TABLE [{table.Schema}].[{table.Name}] (");

            List<string> columnDefinitions = new List<string>();
            foreach (var column in table.Columns)
            {
                columnDefinitions.Add(GenerateColumnDefinition(column));
            }

            // Add primary key constraint if exists
            var primaryKeyColumns = table.Columns.FindAll(c => c.IsPrimaryKey);
            if (primaryKeyColumns.Count > 0)
            {
                string primaryKeyDefinition = $"PRIMARY KEY ({string.Join(", ", primaryKeyColumns.ConvertAll(c => $"[{c.Name}]"))})";
                columnDefinitions.Add(primaryKeyDefinition);
            }

            sb.AppendLine(string.Join(",\n", columnDefinitions));
            sb.AppendLine(");");

            return sb.ToString();
        }

        /// <summary>
        /// Generates a CREATE TABLE script object for a single table.
        /// </summary>
        /// <param name="table">The table to script.</param>
        /// <returns>A script object containing the table's creation script.</returns>
        public static SchemaDesignerScriptObject GenerateCreateAsScriptForTable(SchemaDesignerTable table)
        {
            return new SchemaDesignerScriptObject
            {
                TableId = table.Id,
                Script = GenerateTableDefinition(table)
            };
        }

        /// <summary>
        /// Generates CREATE TABLE script objects for all tables in the schema.
        /// </summary>
        /// <param name="schema">The schema model containing tables to script.</param>
        /// <returns>A list of script objects for all tables.</returns>
        public static List<SchemaDesignerScriptObject> GenerateCreateAsScriptForSchemaTables(SchemaDesignerModel schema)
        {
            List<SchemaDesignerScriptObject> scripts = new List<SchemaDesignerScriptObject>();

            foreach (var table in schema.Tables)
            {
                scripts.Add(GenerateCreateAsScriptForTable(table));
            }

            return scripts;
        }

        /// <summary>
        /// Generates a SQL column definition for a table column.
        /// </summary>
        /// <param name="column">The column to script.</param>
        /// <returns>A string containing SQL for the column definition.</returns>
        public static string GenerateColumnDefinition(SchemaDesignerColumn column)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"[{column.Name}] {column.DataType}");

            // Handle length specification for applicable data types
            if (column.MaxLength.HasValue && column.MaxLength != -1)
            {
                if (IsLengthBasedType(column.DataType))
                {
                    if (IsBytePairDatatype(column.DataType))
                    {
                        sb.Append($"({column.MaxLength / 2})");
                    }
                    else
                    {
                        sb.Append($"({column.MaxLength})");
                    }
                }
            }
            else if (column.MaxLength == -1 && IsLengthBasedType(column.DataType))
            {
                sb.Append("(MAX)");
            }

            // Handle precision and scale only for decimal/numeric types
            if (column.Precision.HasValue && IsPrecisionBasedType(column.DataType))
            {
                if (column.Scale.HasValue)
                {
                    sb.Append($"({column.Precision},{column.Scale})");
                }
                else
                {
                    sb.Append($"({column.Precision})");
                }
            }

            if (!string.IsNullOrEmpty(column.Collation) && column.Collation != "NULL")
            {
                sb.Append($" COLLATE {column.Collation}");
            }


            if (!column.IsNullable)
            {
                sb.Append(" NOT NULL");
            }

            if (column.IsUnique)
            {
                sb.Append(" UNIQUE");
            }

            if (column.IsIdentity)
            {
                sb.Append($" IDENTITY({column.IdentitySeed},{column.IdentityIncrement})");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Determines if a data type uses byte pairs for storage (like nchar, nvarchar).
        /// </summary>
        /// <param name="dataType">The data type to check.</param>
        /// <returns>True if the data type uses byte pairs; otherwise, false.</returns>
        public static bool IsBytePairDatatype(string dataType)
        {
            string[] bytePairTypes = { "nchar", "nvarchar" };
            return bytePairTypes.Contains(dataType.ToLowerInvariant());
        }

        /// <summary>
        /// Determines if a data type supports length specification.
        /// </summary>
        /// <param name="dataType">The data type to check.</param>
        /// <returns>True if the data type supports length specification; otherwise, false.</returns>
        private static bool IsLengthBasedType(string dataType)
        {
            string[] lengthBasedTypes = { "char", "varchar", "nchar", "nvarchar", "binary", "varbinary" };
            return lengthBasedTypes.Contains(dataType.ToLowerInvariant());
        }

        /// <summary>
        /// Determines if a data type supports precision and scale.
        /// </summary>
        /// <param name="dataType">The data type to check.</param>
        /// <returns>True if the data type supports precision and scale; otherwise, false.</returns>
        private static bool IsPrecisionBasedType(string dataType)
        {
            string[] precisionBasedTypes = { "decimal", "numeric", "float", "real" };
            return precisionBasedTypes.Contains(dataType.ToLowerInvariant());
        }

        /// <summary>
        /// Generates SQL scripts for all foreign keys in the schema.
        /// </summary>
        /// <param name="tables">The tables containing foreign keys to script.</param>
        /// <returns>A string containing SQL scripts for all foreign keys.</returns>
        public static string GenerateForeignKeyScripts(List<SchemaDesignerTable> tables)
        {
            StringBuilder sb = new StringBuilder();

            foreach (var table in tables)
            {
                foreach (var fk in table.ForeignKeys)
                {
                    sb.AppendLine(GenerateForeignKeyScript(table, fk));
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Generates a SQL script for a single foreign key.
        /// </summary>
        /// <param name="table">The table containing the foreign key.</param>
        /// <param name="fk">The foreign key to script.</param>
        /// <returns>A string containing SQL script for the foreign key.</returns>

        public static string GenerateForeignKeyScript(SchemaDesignerTable table, SchemaDesignerForeignKey fk)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($@"
ALTER TABLE [{table.Schema}].[{table.Name}]
ADD CONSTRAINT [{fk.Name}]
FOREIGN KEY ({fk.Columns.Select(c => $"[{c}]").Aggregate((a, b) => $"{a}, {b}")}) 
REFERENCES [{fk.ReferencedSchemaName}].[{fk.ReferencedTableName}]({fk.ReferencedColumns.Select(c => $"[{c}]").Aggregate((a, b) => $"{a}, {b}")})
ON DELETE {SchemaDesignerUtils.ConvertOnActionToSql(fk.OnDeleteAction)}
ON UPDATE {SchemaDesignerUtils.ConvertOnActionToSql(fk.OnUpdateAction)};
");
            return sb.ToString();
        }

    }
}
