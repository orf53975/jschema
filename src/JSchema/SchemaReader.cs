﻿// Copyright (c) Microsoft Corporation.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.IO;
using Newtonsoft.Json;

namespace Microsoft.JSchema
{
    public static class SchemaReader
    {
        public static JsonSchema ReadSchema(string jsonText)
        {
            // Change "$ref" to "$$ref" before we ask Json.NET to deserialize it,
            // because Json.NET treats "$ref" specially.
            jsonText = RefProperty.ConvertFromInput(jsonText);

            var serializer = new JsonSerializer
            {
                ContractResolver = new JsonSchemaContractResolver()
            };

            using (var stringReader = new StringReader(jsonText))
            {
                using (var jsonReader = new JsonTextReader(stringReader))
                {
                    return serializer.Deserialize<JsonSchema>(jsonReader);
                }
            }
        }
    }
}