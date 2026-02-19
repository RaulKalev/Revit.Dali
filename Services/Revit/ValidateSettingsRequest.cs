using Autodesk.Revit.UI;
using Dali.Models;
using System;

namespace Dali.Services.Revit
{
    public class ValidateSettingsRequest : IExternalEventRequest
    {
        private readonly ParameterResolver _resolver;
        private readonly SettingsModel _settings;
        private readonly Action<ValidationResult> _callback;

        public ValidateSettingsRequest(ParameterResolver resolver, SettingsModel settings, Action<ValidationResult> callback)
        {
            _resolver = resolver;
            _settings = settings;
            _callback = callback;
        }

        public void Execute(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            var result = _resolver.ValidateSettings(doc, _settings);
            _callback?.Invoke(result);
        }
    }
}
