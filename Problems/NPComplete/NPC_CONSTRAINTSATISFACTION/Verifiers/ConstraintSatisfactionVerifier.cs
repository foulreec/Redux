using System.Text.Json.Serialization;
using System.Globalization;
using API.Interfaces;

namespace API.Problems.NPComplete.NPC_CONSTRAINTSATISFACTION.Verifiers;

class ConstraintSatisfactionVerifier : IVerifier<CONSTRAINTSATISFACTION> {

    // --- Fields ---
    public string verifierName { get; } = "Constraint Satisfaction Problem Verifier";
    public string verifierDefinition { get; } = "This is a verifier for the Constraint Satisfaction Problem. It takes the certificate from the user and validates that every variable assignment satisfies all constraints.";
    public string source { get; } = "";
    public string sourceFile { get; } = SourceFile.Path();
    public string[] contributors { get; } = { "Gabriel Ory" };
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? complexity { get; set; } = null;
    private string _certificate = "";

    public string certificate {
        get {
            return _certificate;
        }
    }

    // --- Methods Including Constructors ---
    public ConstraintSatisfactionVerifier() { }

    public bool verify(CONSTRAINTSATISFACTION problem, string certificate) {

        // Check if the certificate is empty or null, and throw a CertificateParseException if it is.
        if (string.IsNullOrWhiteSpace(certificate)) {
            throw new CertificateParseException(problem, certificate, "certificate is empty");
        }

        var variables = problem.X;
        var domains = problem.D;
        var constraints = problem.C;

        // The expected certificate follows the format (LiteralName = Assignment, LiteralName = Assignment, ...)
        if (!certificate.StartsWith('(') || !certificate.EndsWith(')')) {
            throw new CertificateParseException(problem, certificate,
                "certificate must start with '(' and end with ')'");
        }

        // Parse the certificate into a dictionary of variable assignments.
        var assignments = new Dictionary<string, decimal>();

        // The format of the certificate is expected to be a parenthesized list of variable assignments in the form (x1:v1,x2:v2,...)
        var assignmentPairs = certificate
            .Substring(1, certificate.Length - 2)  // Remove the surrounding parentheses
            .Split(',', StringSplitOptions.RemoveEmptyEntries); // Split by commas to get individual variable assignments

        foreach (var pair in assignmentPairs) {
            // Split each assignment into variable and value parts
            // Part 0 is the variable name, Part 1 is the assigned value.
            var parts = pair.Split(':');

            var variable = parts[0].Trim();

            // Validate that the variable is in the list of variables and that it is assigned only once.
            if (!variables.Contains(variable)) {
                throw new CertificateParseException(problem, certificate,
                    $"variable '{variable}' is not in the list of variables");
            }

            // Check if the variable has already been assigned a value in the certificate.
            if (assignments.ContainsKey(variable)) {
                throw new CertificateParseException(problem, certificate,
                    $"variable '{variable}' is assigned more than once");
            }

            // Validate that the value is a valid decimal 
            if (!decimal.TryParse(
                parts[1].Trim(),
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out decimal value)) {
                throw new CertificateParseException(problem, certificate,
                    $"value '{parts[1]}' is not a valid decimal number");
            }

            // Validate that the value is in the domain of the corresponding variable.
            var variableIndex = 0;

            // Find the index of the variable in the list of variables to check its corresponding domain.
            foreach (var candidate in variables) {
                if (candidate == variable) {
                    break;
                }
                variableIndex++;
            }

            // Validate that the variable index is within bounds and that the value is in the corresponding domain.
            if (variableIndex >= domains.Count || !domains[variableIndex].Contains(value)) {
                throw new CertificateParseException(problem, certificate,
                    $"value '{value}' is not in the domain of variable '{variable}'");
            }
            assignments[variable] = value;
        }

        // Check if all variables have been assigned a value in the certificate.
        if (assignments.Count != variables.Count()) {
            throw new CertificateParseException(problem, certificate,
                "not all variables have been assigned a value");
        }

        // Return false only when the certificate parses successfully but fails the verification predicate; return true when it satisfies it.
        foreach (Constraint constraint in constraints) {
            // Evaluate each constraint with the given variable assignments.
            if (!constraint.Evaluate(assignments)) {
                return false; // If any constraint is not satisfied, return false.
            }
        }
        return true;
    }
}
