set quiet

# Variables
root_folder := "./src"
test_root := root_folder + "/tests"

solution_file := root_folder + "/EventSourcing.slnx"

benchmarks_project := root_folder + "/src/Benchmarks/Benchmarks.csproj"

build_configuration := "Debug"
artifacts_folder := "./artifacts"

pipeline_feed := "https://api.nuget.org/v3/index.json"
pipeline_tool := ".tools/purview-build/purview-build"

current_version := `bun -p "require('./package.json').version"`

[private]
default:
    just --list

# Install the shared Purview.Build tool if not present
[private]
ensure-pipeline-tool:
    if [ ! -x "{{ pipeline_tool }}" ]; then \
        dotnet tool install Purview.Build --tool-path .tools/purview-build --add-source "{{ pipeline_feed }}"; \
    fi

# Run the PR pipeline (restore, build, lint, tests)
[group('Pipeline')]
pipeline-pr *args:
    just ensure-pipeline-tool
    echo "Running PR pipeline..."
    "{{ pipeline_tool }}" {{ args }}

# Run the build pipeline (restore, build, lint)
[group('Pipeline')]
pipeline-build *args:
    just ensure-pipeline-tool
    echo "Running build pipeline..."
    "{{ pipeline_tool }}" --Build:RunTests=false --Release:Mode=None {{ args }}

# Run the release pipeline (restore, build, lint, tests, pack, publish, GitHub release)
[group('Pipeline')]
pipeline-release *args:
    just ensure-pipeline-tool
    echo "Running release pipeline..."
    "{{ pipeline_tool }}" --Release:Mode=NuGet {{ args }}

# Run the release pipeline (restore, build, lint, tests, pack, local nuget publish)
# Note: `just` runs recipes through the shell, which strips backslashes from unquoted arguments.
# Always use forward slashes for the feed path, e.g.
# just pipeline-local-release --PublishLocalNuGet:LocalFeedPath=p:/_sync-projects/.local-nuget/
[group('Pipeline')]
pipeline-local-release *args:
    just ensure-pipeline-tool
    just lint-fix
    echo "Running local release pipeline..."
    "{{ pipeline_tool }}" --Release:Mode=LocalNuGet {{ args }}

# Run the pipeline with tests enabled
[group('Pipeline')]
pipeline-tests *args:
    just ensure-pipeline-tool
    echo "Running tests pipeline..."
    "{{ pipeline_tool }}" --Build:RunTests=true --Release:Mode=None {{ args }}

# Open the solution in Visual Studio/ Registered application
[group('Utilities')]
vs:
    open {{ solution_file }}

# Export the Admin API OpenAPI document to src/src/Admin.Client/OpenApi/admin.openapi.json
[group('Utilities')]
admin-openapi-export:
    dotnet run --project src/tools/AdminAPI.OpenAPI --configuration {{ build_configuration }}

# Regenerate the NSwag Admin API client from the committed OpenAPI document
[group('Utilities')]
admin-client-generate:
    dotnet nswag run src/src/Admin.Client/nswag.json

# Regenerate the OpenAPI document and the NSwag client (run after Admin API contract changes)
[group('Utilities')]
admin-client-regenerate:
    just admin-openapi-export
    just admin-client-generate

# Build the solution for the specified configuration (default: Debug; CI uses the Release pipeline)
[group('Build and Test')]
build *args:
    echo "==> Building {{ BLUE }}{{ solution_file }}{{ NORMAL }} ({{ GREEN }}{{ current_version }}{{ NORMAL }}) with configuration {{ YELLOW }}{{ build_configuration }}{{ NORMAL }}"
    dotnet build {{ solution_file }} --configuration {{ build_configuration }} {{ args }}

# Cleans the solution for the specified configuration (default: Debug)
[group('Build and Test')]
clean *args:
    echo "==> Cleaning {{ BLUE }}{{ solution_file }}{{ NORMAL }} ({{ GREEN }}{{ current_version }}{{ NORMAL }}) with configuration {{ YELLOW }}{{ build_configuration }}{{ NORMAL }}"
    dotnet clean {{ solution_file }} --configuration {{ build_configuration }} {{ args }}

# Restore local .NET tools
[group('Utilities')]
tools:
    dotnet tool restore

# Restore NuGet packages for the solution
[group('Build and Test')]
restore *args:
    dotnet restore {{ solution_file }} {{ args }}

# Displays the current package version from package.json
[group('Build and Test')]
current_version:
    echo "==> Current version: {{ GREEN }}{{ current_version }}{{ NORMAL }} (defined in package.json and automatically included in the build output through the Purview.DotNetProjectSdk package)"

# Run the source-generator performance harness (pass --benchmark for larger runs; requires Release for meaningful numbers)
[group('Performance Tests')]
perf-source-generator *args:
    dotnet run --project {{ benchmarks_project }} --configuration Release -- source-generator {{ args }}

# Run the runtime (generated-code) performance harness (pass --benchmark for larger runs; requires Release for meaningful numbers)
[group('Performance Tests')]
perf-runtime *args:
    dotnet run --project {{ benchmarks_project }} --configuration Release -- runtime {{ args }}

# Run the in-memory store throughput harness (the allocation-free reference; pass --benchmark for larger runs)
[group('Performance Tests')]
perf-inmemory *args:
    dotnet run --project {{ benchmarks_project }} --configuration Release -- inmemory {{ args }}

# Run SQL Server event/snapshot performance harness (pass --benchmark for larger runs; requires Docker/Testcontainers)
[group('Performance Tests')]
perf-sql-server *args:
    dotnet run --project {{ benchmarks_project }} --configuration Release -- sql-server {{ args }}

# Run tests for a specific project with a filter (e.g., "/*/*/*/*", or "/*/*/*/*[Category=Unit]" to run just unit tests) and configuration (e.g., "Release")
[group('Build and Test')]
test filter="/*/*/*/*" *args:
    echo "==> Testing {{ BLUE }}{{ solution_file }}{{ NORMAL }} ({{ GREEN }}{{ build_configuration }}{{ NORMAL }}) with filter {{ YELLOW }}{{ filter }}{{ NORMAL }}"
    dotnet test --project {{ solution_file }} --configuration {{ build_configuration }} --treenode-filter "{{ filter }}" --ignore-exit-code 8 {{ args }}

# Run unit tests for the solution
[group('Build and Test')]
test-unit *args:
    just test "/*/*/*/*[Category=Unit]" {{ args }}

# Pack all packable projects
[group('Build and Test')]
pack artifact_folder=artifacts_folder *args:
    echo "==> Packing {{ BLUE }}{{ solution_file }}{{ NORMAL }} ({{ GREEN }}{{ current_version }}{{ NORMAL }}) to {{ YELLOW }}{{ artifact_folder }}{{ NORMAL }}"
    dotnet pack "{{ solution_file }}" --configuration "{{ build_configuration }}" --output "{{ artifact_folder }}" {{ args }}

# Format the code with CSharpier
[group('Utilities')]
lint-fix:
    dotnet csharpier format .

# Check formatting with CSharpier
[group('Utilities')]
lint-check:
    dotnet csharpier check .

# Clean up the repository by removing build artifacts, bin/obj folders etc, and shutting down the build server
[group('Utilities')]
scrub:
    find . -type d \( -name bin -o -name obj \) -exec rm -rf {} +
    just clean
    just restore --force-evaluate
    dotnet build-server shutdown
