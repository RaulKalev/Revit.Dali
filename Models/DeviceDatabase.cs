using System.Collections.Generic;
using Newtonsoft.Json;

namespace Dali.Models
{
    public class DeviceDatabase
    {
        [JsonProperty("schemaVersion")]
        public string SchemaVersion { get; set; }

        [JsonProperty("manufacturer")]
        public string Manufacturer { get; set; }

        [JsonProperty("defaults")]
        public DeviceDefaults Defaults { get; set; }

        [JsonProperty("devices")]
        public List<DeviceDto> Devices { get; set; }

        [JsonProperty("rules")]
        public List<DeviceRule> Rules { get; set; }
    }

    public class DeviceDefaults
    {
        [JsonProperty("maxAddressesPerDaliLine")]
        public int MaxAddressesPerDaliLine { get; set; }

        [JsonProperty("daliLineLengthMetersStandard")]
        public int DaliLineLengthMetersStandard { get; set; }

        [JsonProperty("daliLineLengthMetersWithRepeater")]
        public int DaliLineLengthMetersWithRepeater { get; set; }
    }

    public class DeviceDto
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("daliLines")]
        public int? DaliLines { get; set; }

        [JsonProperty("maxAddressesPerLine")]
        public int? MaxAddressesPerLine { get; set; }

        [JsonProperty("ratedCurrentmAPerLine")]
        public int? RatedCurrentmAPerLine { get; set; }

        [JsonProperty("guaranteedCurrentmAPerLine")]
        public int? GuaranteedCurrentmAPerLine { get; set; }

        [JsonProperty("addsCurrentmA")]
        public int? AddsCurrentmA { get; set; }

        [JsonProperty("addsAddresses")]
        public int? AddsAddresses { get; set; }

        [JsonProperty("extendsLineLengthMetersTo")]
        public int? ExtendsLineLengthMetersTo { get; set; }

        [JsonProperty("scope")]
        public string Scope { get; set; }
    }

    public class DeviceRule
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("appliesTo")]
        public string AppliesTo { get; set; }

        [JsonProperty("maxAddresses")]
        public int? MaxAddresses { get; set; }
    }
}
