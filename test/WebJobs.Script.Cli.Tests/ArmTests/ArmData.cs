using System;
using WebJobs.Script.Cli.Arm;

namespace WebJobs.Script.Cli.Tests.ArmTests
{
    public class ArmDataEntry
    {
        public string Value { get; set; }
        public string ContentType { get; set; }
        public Uri Uri { get; set; }
    }

    public static class ArmData
    {
        public const string Subscription1 = "first-sub-id";
        public const string Subscription2 = "second-sub-Id";
        public static readonly ArmDataEntry MultipleSubscriptions = new ArmDataEntry
        {
            Uri = ArmUriTemplates.Subscriptions.Bind(string.Empty),
            ContentType = "application/json",
            Value = $@"{{
  'value': [
    {{
      'id': '/subscriptions/{Subscription1}',
      'subscriptionId': '{Subscription1}',
      'displayName': 'sub display name',
      'state': 'Enabled',
      'subscriptionPolicies': {{
        'locationPlacementId': 'Public_2014-09-01',
        'quotaId': 'MSDN_2014-09-01'
      }}
    }},
    {{
      'id': '/subscriptions/{Subscription2}',
      'subscriptionId': '{Subscription2}',
      'displayName': 'sub display name',
      'state': 'Enabled',
      'subscriptionPolicies': {{
        'locationPlacementId': 'Public_2014-09-01',
        'quotaId': 'MSDN_2014-09-01'
      }}
    }}
  ]
}}"
        };
    }
}
