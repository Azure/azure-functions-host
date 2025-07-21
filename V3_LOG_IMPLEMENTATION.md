# V3 Log Disablement Implementation

## Overview
This implementation adds support for disabling V3 logs in Kusto while preserving Application Insights logging functionality. The key principle is to separate Kusto/Linux event generation from Application Insights telemetry processing.

## Changes Made

### 1. Configuration Support
- **ScriptConstants.cs**: Added `HostingConfigDisableV3Logs` constant
- **EnvironmentSettingNames.cs**: Added `FunctionsDisableV3Logs` environment variable
- **FunctionsHostingConfigOptions.cs**: Added `DisableV3Logs` property
- **EnvironmentExtensions.cs**: Added `IsV3LogsDisabled()` extension method
- **FunctionsHostingConfigOptionsSetup.cs**: Enhanced to read from environment variables

### 2. Telemetry Processing
- **V3LogFilterTelemetryProcessor.cs**: New telemetry processor that can filter V3 logs while preserving AppInsights
- **ScriptHostBuilderExtensions.cs**: Registered the new telemetry processor in the Application Insights pipeline

### 3. Event Generator Updates
- **LinuxAppServiceEventGenerator.cs**: Modified logging methods to respect V3 log disablement
- **LinuxContainerEventGenerator.cs**: Modified logging methods to respect V3 log disablement

### 4. Tests
- **FunctionsHostingConfigOptionsTest.cs**: Added tests for new configuration property
- **V3LogFilterTelemetryProcessorTests.cs**: Comprehensive tests for telemetry filtering
- **LinuxAppServiceEventGeneratorTests.cs**: Tests for V3 log disablement in AppService
- **LinuxContainerEventGeneratorV3LogTests.cs**: Tests for V3 log disablement in Container

## How It Works

### Configuration Sources
The V3 log disablement can be configured through:
1. **Hosting Configuration**: `DisableV3Logs=1` in hosting config
2. **Environment Variable**: `FUNCTIONS_DISABLE_V3_LOGS=1`

### Log Separation Strategy
1. **Kusto Logs**: When V3 logs are disabled, Linux event generators stop emitting logs
2. **Application Insights**: Continues to work normally through the telemetry pipeline
3. **Telemetry Processor**: Acts as a safety net for any Kusto-specific telemetry

### Event Flow
```
Application Code → Logger → Telemetry Pipeline
                                │
                                ├─→ Application Insights (Always Active)
                                │
                                └─→ Linux Event Generators (Respects V3 Disable)
                                    └─→ Kusto
```

## Usage

### Enable V3 Log Disablement
```bash
# Through environment variable
export FUNCTIONS_DISABLE_V3_LOGS=1

# Through hosting configuration
# Set DisableV3Logs=1 in hosting config
```

### Expected Behavior
- **When Disabled**: Kusto logs are suppressed, AppInsights continues
- **When Enabled** (default): Both Kusto and AppInsights logs flow normally

## Testing
The implementation includes comprehensive tests covering:
- Configuration parsing and property access
- Telemetry processor filtering behavior
- Event generator respect for configuration
- Both hosting config and environment variable sources

## Backward Compatibility
- Default behavior is unchanged (V3 logs enabled)
- Existing Application Insights configurations continue to work
- No breaking changes to existing APIs