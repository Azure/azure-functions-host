using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.AspNetCore.Routing.Tree;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class RoutingUtility
    {
        private readonly static IDictionary<string, TemplateMatcher> Templates = new Dictionary<string, TemplateMatcher>();
        private readonly static IDictionary<string, IDictionary<string, IRouteConstraint>> ConstraintsMap = new Dictionary<string, IDictionary<string, IRouteConstraint>>();  
        private readonly static IOptions<RouteOptions> RouteOptions = new OptionsWrapper<RouteOptions>(new RouteOptions());
        private readonly static IRouter Router = new FakeRouter();
        private readonly static IInlineConstraintResolver ConstraintResolver = new DefaultInlineConstraintResolver(RouteOptions);
        private readonly static AspNetCore.Http.HttpContext Context = new DefaultHttpContext();

        //a dictionary of route constraints to functions that preform the appropriate conversions
        private static readonly IDictionary<Type, Func<string, object>> StringConverter = new Dictionary
            <Type, Func<string, object>>
        {
            {typeof(BoolRouteConstraint), (str => Convert.ToBoolean(str)) },
            {typeof(DateTimeRouteConstraint), (str => Convert.ToDateTime(str)) },
            {typeof(DecimalRouteConstraint), (str => Convert.ToDecimal(str)) },
            {typeof(DoubleRouteConstraint), (str => Convert.ToDouble(str)) },
            {typeof(FloatRouteConstraint), (str => Convert.ToSingle(str)) },
            {typeof(GuidRouteConstraint), (str => Guid.Parse(str)) },
            {typeof(IntRouteConstraint), (str => Convert.ToInt32(str)) },
            {typeof(LongRouteConstraint), (str => Convert.ToInt64(str)) },
            {typeof(RangeRouteConstraint), (str => Convert.ToInt64(str)) }
        };  

        private static TemplateMatcher GetTemplateMatcher(string queryTemplate)
        {
            TemplateMatcher templateMatcher = null;
            Templates.TryGetValue(queryTemplate, out templateMatcher);
            if (templateMatcher == null)
            {
                var routeTemplate = TemplateParser.Parse(queryTemplate);
                var defaults = new RouteValueDictionary();
                foreach (var parameter in routeTemplate.Parameters)
                {
                    if (parameter.DefaultValue != null)
                    {
                        defaults.Add(parameter.Name, parameter.DefaultValue);
                    }
                }
                SetConstraints(queryTemplate, routeTemplate);
                templateMatcher = new TemplateMatcher(routeTemplate, defaults);
                Templates.Add(queryTemplate, templateMatcher);

            }
            return templateMatcher;
        }

        private static void SetConstraints(string queryTemplate, RouteTemplate parsedTemplate)
        {
            //if the constraints for this route have not already been set, add them to ConstraintsMap
            IDictionary<string, IRouteConstraint> constraints = null;
            ConstraintsMap.TryGetValue(queryTemplate, out constraints);
            if (constraints == null)
            {
                var constraintBuilder = new RouteConstraintBuilder(ConstraintResolver, parsedTemplate.TemplateText);
                foreach (var parameter in parsedTemplate.Parameters)
                {
                    if (parameter.IsOptional)
                    {
                        constraintBuilder.SetOptional(parameter.Name);
                    }

                    foreach (var inlineConstraint in parameter.InlineConstraints)
                    {
                        constraintBuilder.AddResolvedConstraint(parameter.Name, inlineConstraint.Constraint);
                    }
                }
                constraints = constraintBuilder.Build();
                ConstraintsMap.Add(queryTemplate, constraints);
            }
        }


        public static void ClearTemplates()
        {
            Templates.Clear();
            ConstraintsMap.Clear();
        }

        public static bool MatchesTemplate(string queryTemplate, string query)
        {
            try
            {
                var templateMatcher = GetTemplateMatcher(queryTemplate);
                var pathString = new PathString("/" + query);
                var values = new RouteValueDictionary();
                bool success = templateMatcher.TryMatch(pathString, values);
                if (success)
                {
                    var constraints = ConstraintsMap[queryTemplate];
                    foreach (var constraintPair in constraints)
                    {
                        string parameter = constraintPair.Key;
                        IRouteConstraint constraint = constraintPair.Value;
                        if (!constraint.Match(Context, Router, parameter, values, RouteDirection.IncomingRequest))
                        {
                            return false;
                        }
                    }
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

        }

        public static IDictionary<string, object> ExtractRouteParameters(string queryTemplate, HttpRequestMessage request)
        {
            var values = new RouteValueDictionary();

            if (queryTemplate != null)
            {
                string requestUri = request.RequestUri.AbsoluteUri;
                int idx = requestUri.ToLowerInvariant().IndexOf("api", StringComparison.OrdinalIgnoreCase);
                string uri = null;
                if (idx > 0)
                {
                    idx = requestUri.IndexOf('/', idx);
                    uri = requestUri.Substring(idx + 1).Trim('/');
                }

                if (uri != null)
                {
                    var templateMatcher = GetTemplateMatcher(queryTemplate);
                    var uriPath = new PathString("/" + uri);
                    bool success = templateMatcher.TryMatch(uriPath, values);
                    if (success)
                    {
                        var constraints = ConstraintsMap[queryTemplate];
                        foreach (var constraintPair in constraints)
                        {
                            string parameter = constraintPair.Key;
                            IRouteConstraint constraint = constraintPair.Value;
                            values[parameter] = CoerceArgumentType((string)values[parameter], constraint);
                        }
                    }
                    else
                    {
                        return null;
                    }
                }
            }
            return values;
        }

        private static object CoerceArgumentType(string value, IRouteConstraint constraint)
        {
            Func<string, object> conversion;
            StringConverter.TryGetValue(constraint.GetType(), out conversion);
            return conversion != null ? conversion.Invoke(value) : value;     
        }

        public static string ExtractRouteTemplateFromMetadata(FunctionMetadata metadata)
        {
            var inputBindings = metadata.InputBindings;
            var trigger = inputBindings.FirstOrDefault(p => p.Type == BindingType.HttpTrigger) as HttpTriggerBindingMetadata;
            return trigger?.Route;
        }

        //Only used to trick the constraint Match function to not throw an ArgumentNullException. The IRouter object is never actually used.
        private class FakeRouter : IRouter
        {
            public VirtualPathData GetVirtualPath(VirtualPathContext context)
            {
                throw new NotImplementedException();
            }

            public Task RouteAsync(RouteContext context)
            {
                throw new NotImplementedException();
            }
        }
    }
}