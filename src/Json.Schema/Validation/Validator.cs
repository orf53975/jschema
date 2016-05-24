﻿// Copyright (c) Microsoft Corporation.  All Rights Reserved.
// Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Json.Schema.Validation
{
    /// <summary>
    /// Validates a JSON instance against a schema.
    /// </summary>
    public class Validator
    {
        private const string Null = "null";

        private readonly Stack<JsonSchema> _schemas;
        private List<string> _messages;
        private Dictionary<string, JsonSchema> _definitions;

        /// <summary>
        /// Initializes a new instance of the <see cref="Validator"/> class.
        /// </summary>
        /// <param name="schema">
        /// The JSON schema to use for validation.
        /// </param>
        public Validator(JsonSchema schema)
        {
            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            _definitions = schema.Definitions;

            schema = Resolve(schema);

            _messages = new List<string>();

            _schemas = new Stack<JsonSchema>();
            _schemas.Push(schema);
        }

        public string[] Validate(string instanceText)
        {
            _messages = new List<string>();

            using (var reader = new StringReader(instanceText))
            {
                JToken token = JToken.ReadFrom(new JsonTextReader(reader));
                JsonSchema schema = _schemas.Peek();

                ValidateToken(token, schema);
            }

            return _messages.ToArray();
        }

        private JsonSchema Resolve(JsonSchema schema)
        {
            if (schema.Reference != null)
            {
                schema = _definitions[schema.Reference.GetDefinitionName()];
            }

            return schema;
        }

        private void ValidateToken(JToken jToken, JsonSchema schema)
        {
            if (!TokenTypeIsCompatibleWithSchema(jToken.Type, schema.Type))
            {
                AddMessage(jToken, ErrorNumber.WrongType, FormatSchemaTypes(schema.Type), jToken.Type);
                return;
            }

            switch (jToken.Type)
            {
                case JTokenType.String:
                    ValidateString((JValue)jToken, schema);
                    break;

                case JTokenType.Integer:
                case JTokenType.Float:
                    ValidateNumber((JValue)jToken, schema);
                    break;

                case JTokenType.Object:
                    ValidateObject((JObject)jToken, schema);
                    break;

                case JTokenType.Array:
                    ValidateArray((JArray)jToken, schema);
                    break;

                default:
                    break;
            }

            if (schema.Enum != null)
            {
                ValidateEnum(jToken, schema.Enum);
            }

            if (schema.AllOf != null)
            {
                ValidateAllOf(jToken, schema.AllOf);
            }

            if (schema.AnyOf != null)
            {
                ValidateAnyOf(jToken, schema.AnyOf);
            }

            if (schema.OneOf != null)
            {
                ValidateOneOf(jToken, schema.OneOf);
            }

            if (schema.Not != null)
            {
                ValidateNot(jToken, schema.Not);
            }
        }

        private static bool TokenTypeIsCompatibleWithSchema(JTokenType instanceType, IList<JTokenType> schemaTypes)
        {
            if (schemaTypes == null || !schemaTypes.Any())
            {
                return true;
            }

            if (schemaTypes.Contains(instanceType))
            {
                return true;
            }

            if (instanceType == JTokenType.Integer && schemaTypes.Contains(JTokenType.Float))
            {
                return true;
            }

            if (instanceType == JTokenType.Date && schemaTypes.Contains(JTokenType.String))
            {
                return true;
            }

            return false;
        }

        private void ValidateString(JValue jValue, JsonSchema schema)
        {
            if (schema.MaxLength.HasValue)
            {
                string value = jValue.Value<string>();

                if (value.Length > schema.MaxLength)
                {
                    AddMessage(jValue, ErrorNumber.StringTooLong, value, value.Length, schema.MaxLength);
                }
            }

            if (schema.MinLength.HasValue)
            {
                string value = jValue.Value<string>();

                if (value.Length < schema.MinLength)
                {
                    AddMessage(jValue, ErrorNumber.StringTooShort, value, value.Length, schema.MinLength);
                }
            }

            if (schema.Pattern != null)
            {
                string value = jValue.Value<string>();

                if (!Regex.IsMatch(value, schema.Pattern))
                {
                    AddMessage(jValue, ErrorNumber.StringDoesNotMatchPattern, value, schema.Pattern);
                }
            }
        }

        private void ValidateNumber(JValue jValue, JsonSchema schema)
        {
            if (schema.Maximum.HasValue)
            {
                double maximum = schema.Maximum.Value;
                double value = jValue.Type == JTokenType.Float
                    ? (double)jValue.Value
                    : (long)jValue.Value;

                if (schema.ExclusiveMaximum == true && value >= maximum)
                {
                    AddMessage(jValue, ErrorNumber.ValueTooLargeExclusive, value, maximum);
                }
                else if (value > maximum)
                {
                    AddMessage(jValue, ErrorNumber.ValueTooLarge, value, maximum);
                }
            }

            if (schema.Minimum.HasValue)
            {
                double minimum = schema.Minimum.Value;
                double value = jValue.Type == JTokenType.Float
                    ? (double)jValue.Value
                    : (long)jValue.Value;

                if (schema.ExclusiveMinimum == true && value <= minimum)
                {
                    AddMessage(jValue, ErrorNumber.ValueTooSmallExclusive, value, minimum);
                }
                else if (value < minimum)
                {
                    AddMessage(jValue, ErrorNumber.ValueTooSmall, value, minimum);
                }
            }

            if (schema.MultipleOf.HasValue)
            {
                double factor = schema.MultipleOf.Value;
                double value = jValue.Type == JTokenType.Float
                    ? (double)jValue.Value
                    : (long)jValue.Value;

                if (value % factor != 0)
                {
                    AddMessage(jValue, ErrorNumber.NotAMultiple, value, factor);
                }
            }
        }

        private void ValidateArray(JArray jArray, JsonSchema schema)
        {
            int numItems = jArray.Count;
            if (numItems < schema.MinItems)
            {
                AddMessage(jArray, ErrorNumber.TooFewArrayItems, schema.MinItems, numItems);
            }

            if (numItems > schema.MaxItems)
            {
                AddMessage(jArray, ErrorNumber.TooManyArrayItems, schema.MaxItems, numItems);
            }

            if (schema.Items != null)
            {
                if (schema.Items.SingleSchema)
                {
                    foreach (JToken jToken in jArray)
                    {
                        ValidateToken(jToken, Resolve(schema.Items.Schema));
                    }
                }
                else
                {
                    // TODO: Use additionalItems if available.
                    if (schema.Items.Schemas.Count >= jArray.Count)
                    {
                        int i = 0;
                        foreach (JToken jToken in jArray)
                        {
                            ValidateToken(jToken, Resolve(schema.Items.Schemas[i++]));
                        }
                    }
                    else
                    {
                        AddMessage(jArray, ErrorNumber.TooFewItemSchemas, jArray.Count, schema.Items.Schemas.Count);
                    }
                }
            }

            if (schema.UniqueItems == true)
            {
                ValidateUnique(jArray);
            }
        }

        private void ValidateObject(JObject jObject, JsonSchema schema)
        {
            List<string> propertySet = jObject.Properties().Select(p => p.Name).ToList();

            if (schema.MaxProperties.HasValue &&
                propertySet.Count > schema.MaxProperties.Value)
            {
                AddMessage(jObject, ErrorNumber.TooManyProperties, schema.MaxProperties.Value, propertySet.Count);
            }

            if (schema.MinProperties.HasValue &&
                propertySet.Count < schema.MinProperties.Value)
            {
                AddMessage(jObject, ErrorNumber.TooFewProperties, schema.MinProperties.Value, propertySet.Count);
            }

            // Ensure required properties are present.
            if (schema.Required != null)
            {
                IEnumerable<string> missingProperties = schema.Required.Except(propertySet);
                foreach (string propertyName in missingProperties)
                {
                    AddMessage(jObject, ErrorNumber.RequiredPropertyMissing, propertyName);
                }
            }

            // Ensure each property matches its schema.
            if (schema.Properties != null)
            {
                foreach (string propertyName in propertySet)
                {
                    JsonSchema propertySchema;
                    if (schema.Properties.TryGetValue(propertyName, out propertySchema))
                    {
                        propertySchema = Resolve(propertySchema);
                        JProperty property = jObject.Property(propertyName);
                        ValidateToken(property.Value, propertySchema);
                    }
                }
            }

            // Does the object contain any properties not specified by the schema?
            List<string> additionalPropertyNames = schema.Properties == null
                ? propertySet
                : propertySet.Except(schema.Properties.Keys).ToList();

            if (schema.PatternProperties != null)
            {
                foreach (string patternRegEx in schema.PatternProperties.Keys)
                {
                    List<string> matchingPropertyNames = additionalPropertyNames.Where(p => Regex.IsMatch(p, patternRegEx)).ToList();

                    if (matchingPropertyNames.Any())
                    {
                        JsonSchema propertySchema = schema.PatternProperties[patternRegEx];
                        foreach (string matchingProperty in matchingPropertyNames)
                        {
                            JProperty property = jObject.Property(matchingProperty);
                            ValidateToken(property.Value, propertySchema);
                        }
                    }

                    additionalPropertyNames = additionalPropertyNames.Except(matchingPropertyNames).ToList();
                }
            }

            // If additional properties are not allowed, ensure there are none.
            // Additional properties are allowed by default.
            if (schema.AdditionalProperties != null)
            {
                ValidateAdditionalProperties(jObject, additionalPropertyNames, schema.AdditionalProperties);
            }
        }

        private void ValidateEnum(JToken jToken, IList<object> @enum)
        {
            if (!TokenMatchesEnum(jToken, @enum))
            {
                AddMessage(
                    jToken,
                    ErrorNumber.InvalidEnumValue,
                    FormatObject(jToken),
                    string.Join(", ", @enum.Select(e => FormatObject(e))));
            }
        }

        private void ValidateAllOf(
            JToken jToken,
            IList<JsonSchema> allOfSchemas)
        {
            var allOfErrorMessages = new List<string>();

            foreach (JsonSchema allOfSchema in allOfSchemas)
            {
                JsonSchema schema = Resolve(allOfSchema);
                var allOfValidator = new Validator(schema);
                allOfValidator.ValidateToken(jToken, schema);
                allOfErrorMessages.AddRange(allOfValidator._messages);
            }

            if (allOfErrorMessages.Any())
            {
                AddMessage(jToken, ErrorNumber.NotAllOf, allOfSchemas.Count);
            }
        }

        private void ValidateAnyOf(
            JToken jToken,
            IList<JsonSchema> anyOfSchemas)
        {
            bool valid = false;

            // Since this token is valid if it's valid against *any* of the schemas,
            // we can't just call ValidateToken against each schema. If we did that,
            // we'd accumulate errors from all the failed schemas, only to find out
            // that they weren't really errors at all. So we instantiate a new
            // validator for each schema, and only report the errors from the current
            // validator if they *all* fail.
            foreach (JsonSchema anyOfSchema in anyOfSchemas)
            {
                JsonSchema schema = Resolve(anyOfSchema);
                var anyOfValidator = new Validator(schema);
                anyOfValidator.ValidateToken(jToken, schema);
                if (!anyOfValidator._messages.Any())
                {
                    valid = true;
                    break;
                }
            }

            if (!valid)
            {
                AddMessage(jToken, ErrorNumber.NotAnyOf, anyOfSchemas.Count);
            }
        }

        private void ValidateOneOf(
            JToken jToken,
            IList<JsonSchema> oneOfSchemas)
        {
            int numValid = 0;

            // Since this token is valid if it's valid against *exactly one* of the schemas,
            // we can't just call ValidateToken against each schema. If we did that,
            // we'd accumulate errors from all the failed schemas, only to find out
            // that they weren't really errors at all. So we instantiate a new
            // validator for each schema, and only report an error from the current
            // validator if *all but one* fail.
            foreach (JsonSchema oneOfSchema in oneOfSchemas)
            {
                JsonSchema schema = Resolve(oneOfSchema);
                var oneOfValidator = new Validator(schema);
                oneOfValidator.ValidateToken(jToken, schema);
                if (!oneOfValidator._messages.Any())
                {
                    ++numValid;
                }
            }

            if (numValid != 1)
            {
                AddMessage(jToken, ErrorNumber.NotOneOf, numValid, oneOfSchemas.Count);
            }
        }

        private void ValidateNot(JToken jToken, JsonSchema notSchema)
        {
            // Since this token is valid if it's *not* valid against the schema, we can't
            // just call ValidateToken against this schema. If we did that, we'd
            // accumulate and report the errors from this schema, even though we want
            // there to be at least one error. So we instantiate a new validator for this
            // schema, and only report an error from the current validator if
            // validation against this schema *succeeds*.
            JsonSchema schema = Resolve(notSchema);
            var notValidator = new Validator(schema);
            notValidator.ValidateToken(jToken, schema);
            if (!notValidator._messages.Any())
            {
                AddMessage(jToken, ErrorNumber.ValidatesAgainstNotSchema);
            }
        }

        private void ValidateAdditionalProperties(
            JObject jObject,
            List<string> additionalPropertyNames,
            AdditionalProperties additionalProperties)
        {
            if (!additionalProperties.Allowed)
            {
                foreach (string propertyName in additionalPropertyNames)
                {
                    JProperty property = jObject.Property(propertyName);
                    AddMessage(property, ErrorNumber.AdditionalPropertiesProhibited, propertyName);
                }
            }
            else
            {
                // Additional properties are allowed. If there is a schema to which they
                // must conform, ensure that they do.
                if (additionalProperties.Schema != null)
                {
                    JsonSchema resolvedSchema = Resolve(additionalProperties.Schema);
                    foreach (string propertyName in additionalPropertyNames)
                    {
                        JProperty property = jObject.Property(propertyName);
                        ValidateToken(property.Value, resolvedSchema);
                    }
                }
            }
        }

        private void ValidateUnique(JArray jArray)
        {
            if (jArray.Distinct(JTokenEqualityComparer.Instance).Count() != jArray.Count)
            {
                AddMessage(jArray, ErrorNumber.NotUnique);
            }
        }

        private bool TokenMatchesEnum(JToken jToken, IList<object> @enum)
        {
            return @enum.Any(e => JTokenEqualityComparer.DeepEquals(jToken, e));
        }

        private static string FormatSchemaTypes(IList<JTokenType> schemaTypes)
        {
            return string.Join(", ", schemaTypes.Select(t => t.ToString()));
        }

        private static string FormatObject(object obj)
        {
            if (obj is JToken)
            {
                return FormatToken(obj as JToken);
            }

            if (obj == null)
            {
                return Null;
            }

            string formattedObject = obj.ToString();

            if (obj is string)
            {
                formattedObject = FormatQuotedString(formattedObject);
            }
            else if (obj is bool)
            {
                formattedObject = FormatBoolean(formattedObject);
            }

            return formattedObject;
        }

        private static string FormatToken(JToken jToken)
        {
            string formattedToken = jToken.ToString();

            switch (jToken.Type)
            {
                case JTokenType.String:
                    formattedToken = FormatQuotedString(formattedToken);
                    break;

                case JTokenType.Boolean:
                    formattedToken = FormatBoolean(formattedToken);
                    break;

                case JTokenType.Array:
                    formattedToken = FormatArray(formattedToken);
                    break;

                case JTokenType.Null:
                    formattedToken = Null;
                    break;

                default:
                    break;
            }

            return formattedToken;
        }

        private static string FormatQuotedString(string s)
        {
            return '"' + s + '"';
        }

        private static string FormatBoolean(string s)
        {
            return s.ToLowerInvariant();
        }

        private static string FormatArray(string s)
        {
            s = Regex.Replace(s, @"\[\s+", @"[");
            s = Regex.Replace(s, @",\s+", ", ");
            s = Regex.Replace(s, @"\s+\]", "]");

            return s;
        }

        private void AddMessage(JToken jToken, ErrorNumber errorNumber, params object[] args)
        {
            var error = new Error(jToken, errorNumber, args);
            _messages.Add(error.Message);
        }
    }
}
