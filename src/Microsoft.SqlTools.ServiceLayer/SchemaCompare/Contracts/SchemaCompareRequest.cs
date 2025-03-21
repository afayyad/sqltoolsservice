﻿//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

#nullable disable
using System.Collections.Generic;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Compare;
using Microsoft.SqlTools.Hosting.Protocol.Contracts;
using Microsoft.SqlTools.ServiceLayer.Connection.Contracts;
using Microsoft.SqlTools.ServiceLayer.DacFx.Contracts;
using Microsoft.SqlTools.ServiceLayer.TaskServices;
using Microsoft.SqlTools.ServiceLayer.Utility;

namespace Microsoft.SqlTools.ServiceLayer.SchemaCompare.Contracts
{
    /// <summary>
    /// Types of schema compare endpoints
    /// </summary>
    public enum SchemaCompareEndpointType
    {
        Database = 0,
        Dacpac = 1,
        Project = 2
        // must be kept in-sync with SchemaCompareEndpointType in Azure Data Studio
        // located at \extensions\mssql\src\mssql.d.ts and in the MSSQL for VSCode Extension
        // located at \typings\vscode-mssql.d.ts
    }

    /// <summary>
    /// Info needed from endpoints for schema comparison
    /// </summary>
    public class SchemaCompareEndpointInfo
    {
        /// <summary>
        /// Gets or sets the type of the endpoint
        /// </summary>
        public SchemaCompareEndpointType EndpointType { get; set; }

        /// <summary>
        /// Gets or sets the project file path
        /// </summary>
        public string ProjectFilePath { get; set; }

        /// <summary>
        /// Gets or sets the scripts included in project
        /// </summary>
        public string[] TargetScripts { get; set; }

        /// <summary>
        /// Gets or sets the project data schema provider
        /// </summary>
        public string DataSchemaProvider { get; set; }

        /// <summary>
        /// Gets or sets package filepath
        /// </summary>
        public string PackageFilePath { get; set; }

        /// <summary>
        /// Gets or sets name for the database
        /// </summary>
        public string DatabaseName { get; set; }

        /// <summary>
        /// Connection uri
        /// </summary>
        public string OwnerUri { get; set; }

        /// <summary>
        /// Connection details
        /// </summary>
        public ConnectionDetails ConnectionDetails { get; set; }

        /// <summary>
        /// Extract target of the project used when extracting a database to file system or updating the project from database
        /// </summary>
        public DacExtractTarget? ExtractTarget { get; set; }
    }

    /// <summary>
    /// Parameters for a schema compare request.
    /// </summary>
    public class SchemaCompareParams
    {
        /// <summary>
        /// Operation id of the schema compare operation
        /// </summary>
        public string OperationId { get; set; }

        /// <summary>
        /// Gets or sets the source endpoint info
        /// </summary>
        public SchemaCompareEndpointInfo SourceEndpointInfo { get; set; }

        /// <summary>
        /// Gets or sets the target endpoint info
        /// </summary>
        public SchemaCompareEndpointInfo TargetEndpointInfo { get; set; }

        /// <summary>
        /// Executation mode for the operation. Default is execution
        /// </summary>
        public TaskExecutionMode TaskExecutionMode { get; set; }

        /// <summary>
        /// gets or sets the deployment options for schema compare
        /// </summary>
        public DeploymentOptions DeploymentOptions { get; set; }
    }

    /// <summary>
    /// Parameters returned from a schema compare request.
    /// </summary>
    public class SchemaCompareResult : ResultStatus
    {
        public string OperationId { get; set; }

        public bool AreEqual { get; set; }

        public List<DiffEntry> Differences { get; set; }
    }

    public class DiffEntry
    {
        public SchemaUpdateAction UpdateAction { get; set; }
        public SchemaDifferenceType DifferenceType { get; set; }
        public string Name { get; set; }
        public string[] SourceValue { get; set; }
        public string[] TargetValue { get; set; }
        public DiffEntry Parent { get; set; }
        public List<DiffEntry> Children { get; set; }
        public string SourceScript { get; set; }
        public string TargetScript { get; set; }
        public string SourceObjectType { get; set; }
        public string TargetObjectType { get; set; }
        public bool Included { get; set; }
    }

    /// <summary>
    /// Defines the Schema Compare request type
    /// </summary>
    class SchemaCompareRequest
    {
        public static readonly RequestType<SchemaCompareParams, SchemaCompareResult> Type =
            RequestType<SchemaCompareParams, SchemaCompareResult>.Create("schemaCompare/compare");
    }
}
