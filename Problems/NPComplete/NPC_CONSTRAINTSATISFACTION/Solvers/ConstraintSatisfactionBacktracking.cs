using API.Interfaces;

namespace API.Problems.NPComplete.NPC_CONSTRAINTSATISFACTION.Solvers;

class ConstraintSatisfactionSolver : ISolver<CONSTRAINTSATISFACTION> {

    // --- Fields ---
    public string solverName { get; } = "Constraint Satisfaction Problem Solver";
    public string solverDefinition { get; } = "This is a solver for the Constraint Satisfaction Problem. It uses a backtracking algorithm to find a complete assignment of values to variables that satisfies all constraints.";
    public string source { get; } = "";
    public string sourceFile { get; } = SourceFile.Path();
    public string[] contributors { get; } = { "Gabriel Ory" };
    public SolverType solverType { get; } = SolverType.Backtracking;
    // public SolverComplexityBucket complexityBucket {get;} = SolverComplexityBucket.TODO;
    // public string complexity {get;} = "TODO";
    public bool timerHasExpired { get; set; }

    // --- Methods Including Constructors ---
    public ConstraintSatisfactionSolver() { }

    // Check if all constraints are satisfied with the given variable assignments.
    // Doesn't matter if not all variables have been assigned, just checks the constraints that are ready to be evaluated.
    private static bool Continue(IReadOnlyList<Constraint> constraints, IReadOnlyDictionary<string, decimal> assigned) {
        foreach (var constraint in constraints) {
            if (constraint.IsReady(assigned)) {
                if (!constraint.Evaluate(assigned)) {
                    return false;
                }
            }
        }
        return true;
    }

    // Recursive backtracking function to solve the CSP.
    private string SolveRecursively(IEnumerable<string> variables, IReadOnlyList<IReadOnlyList<decimal>> domains, IReadOnlyList<Constraint> constraints, Dictionary<string, decimal> assignments, int index) {
        // Make sure to check timerHasExpired occasionally, and return if it has
        if (timerHasExpired) { return "timeout"; }

        // Check if all variables have been assigned, and stop the recursion if they have.
        if (index == variables.Count()) {
            return "success"; // All constraints are satisfied.
        }

        // Get the current variable and its corresponding domain.
        var variable = variables.ElementAt(index);
        var domain = domains[index];

        // Try each value in the domain for the current variable.
        foreach (var value in domain) {
            assignments[variable] = value;

            // Check if the current assignments satisfy all constraints that are ready to be evaluated.
            if (Continue(constraints, assignments)) {
                var result = SolveRecursively(variables, domains, constraints, assignments, index + 1);
                // Try assigning the next variable.
                if (result == "success" || result == "timeout") {
                    return result; // A solution was found or the timer has expired, so return the result.
                }
            }

            // Undo the assignment and try another value.
            assignments.Remove(variable);
        }
        return "failure"; // No value in the domain led to a solution
    }

    // Solve the given constraint satisfaction problem using backtracking.
    public string solve(CONSTRAINTSATISFACTION problem) {
        // implement constraint satisfaction solver for CONSTRAINTSATISFACTION

        // get variables, domains, and constraints from problem
        var variables = problem.X;
        var domains = problem.D;
        var constraints = problem.C;

        // initialize a dictionary to hold the variable assignments
        var assignments = new Dictionary<string, decimal>();

        // implement backtracking algorithm to find a solution
        var result = SolveRecursively(variables, domains, constraints, assignments, 0);
        if (result == "success") {
            // If a solution is found, format the assignments as a string in the expected certificate format.
            var assignmentStrings = assignments.Select(kvp => $"{kvp.Key}:{kvp.Value}");
            return $"({string.Join(",", assignmentStrings)})";
        } else if (result == "timeout") {
            return "timeout"; // Return "timeout" if the timer has expired during the search.
        } else {
            return "failure"; // Return "failure" if no solution was found.
        }
    }
}
