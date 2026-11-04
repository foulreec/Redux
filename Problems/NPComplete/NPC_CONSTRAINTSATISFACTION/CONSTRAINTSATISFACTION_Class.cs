using System.Globalization;
using System.Text.RegularExpressions;
using API.Interfaces;
using API.Problems.NPComplete.NPC_CONSTRAINTSATISFACTION.Solvers;
using API.Problems.NPComplete.NPC_CONSTRAINTSATISFACTION.Verifiers;
using API.Problems.NPComplete.NPC_CONSTRAINTSATISFACTION.Visualizations;

namespace API.Problems.NPComplete.NPC_CONSTRAINTSATISFACTION;

class CONSTRAINTSATISFACTION : IProblem<ConstraintSatisfactionSolver, ConstraintSatisfactionVerifier, ConstraintSatisfactionVisualization> {
    public string problemName { get; } = "Constraint Satisfaction Problem";
    public string problemLink { get; } = "https://en.wikipedia.org/wiki/Constraint_satisfaction_problem";
    public string formalDefinition { get; } = "CONSTRAINTSATISFACTION = {<X,D,C> | there exists an assignment a with a(X[i]) in D[i] for every i, satisfying every constraint in C}";
    public string problemDefinition { get; } = "Given distinct variables, an explicitly listed finite nonempty integer domain for each variable, and constraints, determine whether values can be assigned to all variables so that every constraint is satisfied.";
    public string inputDescription { get; } = "X, an ordered list of distinct variables; D, their corresponding finite integer domains; and C, a set of quoted constraint expressions";
    public string outputDescription { get; } = "True or False, whether a complete assignment from the specified domains satisfies all constraints";
    public string source { get; } = "U. Montanari, “Networks of Constraints: Fundamental Properties and Applications to Picture Processing,” Information Sciences, vol. 7, pp. 95–132, Jan. 1974.";
    public string sourceLink { get; } = "https://ics.uci.edu/~dechter/courses/ics-276/spring-19/reading/montanari.pdf";
    public string sourceFile { get; } = SourceFile.Path();
    public string[] contributors { get; } = { "Reece Foulkrod", "Gabriel Ory", "Ziming Wang" };

    private const string DefaultInstance = "((x,y),({1,2},{1,2}),{\"x!=y\"})";
    public string defaultInstance { get; } = DefaultInstance;
    public string instanceFormat { get; } = "Format: (X,D,C). X is a parenthesized list of distinct identifiers; D is a parenthesized list of nonempty sets of 32-bit integers, one per variable in the same order; C is a set of double-quoted constraint expressions. Identifiers use letters, digits and underscores, starting with a letter or underscore. Example: " + DefaultInstance;
    public string certificateFormat { get; } = "A parenthesized list of variable assignments in the form (x1:v1,x2:v2,...), where each xi is a variable from X and each vi is a value from the corresponding domain in D. Example: (x:1,y:2)";
    public string instance { get; set; } = string.Empty;
    public string wikiName { get; } = "Constraint_satisfaction_problem";
    public ConstraintSatisfactionSolver defaultSolver { get; } = new ConstraintSatisfactionSolver();
    public ConstraintSatisfactionVerifier defaultVerifier { get; } = new ConstraintSatisfactionVerifier();
    public ConstraintSatisfactionVisualization defaultVisualization { get; } = new ConstraintSatisfactionVisualization();
    public ComplexityClass complexityClass { get; } = ComplexityClass.NPComplete;
    public ProblemType problemType { get; } = ProblemType.MathematicalProgramming;

    public IEnumerable<string> X { get; }
    public IReadOnlyList<IReadOnlyList<decimal>> D { get; }
    public IReadOnlyList<Constraint> C { get; }

    public CONSTRAINTSATISFACTION() : this(DefaultInstance) { }

    public CONSTRAINTSATISFACTION(string stringInstance) {
        if (string.IsNullOrWhiteSpace(stringInstance))
            throw new ProblemParseException(problemName, stringInstance, "instance is empty");

        try {
            var sections = SplitCollection(stringInstance, '(', ')');
            if (sections.Count != 3)
                throw new FormatException("Instance must contain exactly three sections: (X,D,C).");

            // Parse variables as distinct identifiers, ensuring they are valid and distinct.   
            var variables = SplitCollection(sections[0], '(', ')');

            if (variables.Any(v => !Regex.IsMatch(v, @"\A[A-Za-z_][A-Za-z0-9_]*\z")))
                throw new FormatException("Each variable must be an identifier.");

            if (variables.Distinct(StringComparer.Ordinal).Count() != variables.Count)
                throw new FormatException("Variables must be distinct.");

            // Parse domains as sets of distinct decimal values, ensuring they are nonempty and correspond to the variables.
            var domains = SplitCollection(sections[1], '(', ')');

            if (domains.Count != variables.Count)
                throw new FormatException("There must be exactly one domain per variable.");

            var parsedDomains = new List<IReadOnlyList<decimal>>();

            foreach (string domain in domains) {
                var values = SplitCollection(domain, '{', '}');

                // Ensure that the domain is nonempty.
                if (values.Count == 0)
                    throw new FormatException("Variable domains must be nonempty.");

                var decimals = new HashSet<decimal>();

                // Parse each value as a decimal, ensuring they are distinct.
                foreach (string value in values) {
                    if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out decimal decimalValue))
                        throw new FormatException($"'{value}' is not a decimal domain value.");

                    if (!decimals.Add(decimalValue))
                        throw new FormatException("Domain values must be distinct.");
                }

                parsedDomains.Add(decimals.ToList().AsReadOnly());
            }

            // Parse constraints as quoted expressions, and compile them into functions.
            var constraints = new List<Constraint>();
            foreach (string constraint in SplitCollection(sections[2], '{', '}')) {
                // Validate that the constraint is a nonempty double-quoted expression.
                // It must start and end with a double quote, and the interior cannot contain any double quotes or be empty/whitespace.
                if (constraint.Length < 3 ||
                    constraint[0] != '"' ||
                    constraint[^1] != '"' ||
                    constraint[1..^1].Contains('"') ||
                    string.IsNullOrWhiteSpace(constraint[1..^1])) {
                    throw new FormatException(
                        "Each constraint must be a nonempty double-quoted expression.");
                }
                // Extract the interior of the quoted expression for conversion to a function.
                string expression = constraint[1..^1];

                // Parse and convert the expression.
                constraints.Add(Constraint.Compile(expression));
            }

            // Store the parsed variables, domains, and constraints as read-only lists, and set the instance string.
            X = variables.AsReadOnly();
            D = parsedDomains.AsReadOnly();
            C = constraints.AsReadOnly();

            instance = stringInstance;
        } catch (Exception ex) when (ex is not ProblemParseException) {
            throw new ProblemParseException(problemName, stringInstance, ex.Message, ex);
        }
    }

    // Split top-level commas, preserving domain nesting and quoted expressions.
    private static List<string> SplitCollection(string text, char open, char close) {
        text = text.Trim();
        // Ensure the text starts and ends with the specified open and close characters.
        if (text.Length < 2 || text[0] != open || text[^1] != close)
            throw new FormatException($"Expected a collection enclosed in '{open}' and '{close}'.");

        // If the interior is empty or whitespace, return an empty list.
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text[1..^1]))
            return result;

        var brackets = new Stack<char>();
        bool quoted = false;
        int start = 1;
        for (int i = 1; i < text.Length - 1; i++) {
            char current = text[i];
            if (current == '"') {
                quoted = !quoted;
            } else if (!quoted) {
                if (current is '(' or '{') {
                    brackets.Push(current == '(' ? ')' : '}');
                } else if (current is ')' or '}') {
                    if (brackets.Count == 0 || brackets.Pop() != current)
                        throw new FormatException("Mismatched collection delimiters.");
                } else if (current == ',' && brackets.Count == 0) {
                    result.Add(text[start..i].Trim());
                    start = i + 1;
                }
            }
        }
        if (quoted || brackets.Count != 0)
            throw new FormatException("Unclosed quote or collection delimiter.");
        result.Add(text[start..^1].Trim());
        if (result.Any(string.IsNullOrEmpty))
            throw new FormatException("Collection elements cannot be empty.");
        return result;
    }
}