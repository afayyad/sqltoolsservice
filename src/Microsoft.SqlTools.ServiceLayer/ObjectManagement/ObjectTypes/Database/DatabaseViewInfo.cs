
//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//
#nullable disable

namespace Microsoft.SqlTools.ServiceLayer.ObjectManagement
{
    public class DatabaseViewInfo : SqlObjectViewInfo
    {
        public string[] DatabaseNames { get; set; }
        public string[] LoginNames { get; set; }
        public string[] CollationNames { get; set; }
        public string[] CompatibilityLevels { get; set; }
    }
}