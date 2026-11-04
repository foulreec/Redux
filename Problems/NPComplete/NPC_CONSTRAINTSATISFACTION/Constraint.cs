using System.Globalization;
using System.Linq.Expressions;

namespace API.Problems.NPComplete.NPC_CONSTRAINTSATISFACTION;

class Constraint {
    // --- Fields ---
    // The compiled function that evaluates the constraint.
    private readonly Func<
        IReadOnlyDictionary<string, decimal>, bool> constraintFunction;

    // The set of variables that this constraint depends on.
    public IReadOnlyCollection<string> Variables { get; }

    //  --- Methods---
    // Constraints are created through the Compile method, which parses the text representation of the constraint.
    // Accepts a function and a set of variables, ensuring that the variables are distinct.
    private Constraint(Func<IReadOnlyDictionary<string, decimal>, bool> function, IEnumerable<string> variables) {
        constraintFunction = function;
        Variables = Array.AsReadOnly(
            variables.Distinct(StringComparer.Ordinal).ToArray());
    }

    // Checks if the constraint is ready to be evaluated, meaning that all variables it depends on have been assigned values.
    public bool IsReady(
        IReadOnlyDictionary<string, decimal> assigned) {
        return Variables.All(assigned.ContainsKey);
    }

    // Evaluates the constraint with the given variable assignments.
    public bool Evaluate(IReadOnlyDictionary<string, decimal> assigned) {
        // If not all variables have been assigned, throw an exception to indicate that the constraint cannot be evaluated yet.
        if (!IsReady(assigned))
            throw new InvalidOperationException(
                "Not all variables have been assigned.");

        return constraintFunction(assigned);
    }

    // Compiles a constraint from its text representation into a Constraint object.
    public static Constraint Compile(string text) {
        // Ensure the input text is not empty or whitespace.
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("Constraint cannot be empty.");

        var parser = new Parser(text);
        var function = parser.BuildConstraint().Compile();
        return new Constraint(function, parser.variables);
    }

    // Type of symbols used in the constraint expressions
    private enum SymbolType {
        End,
        Number,
        VariableName,
        Plus,
        Minus,
        Multiply,
        Divide,
        LeftParenthesis,
        RightParenthesis,
        LeftBracket,
        RightBracket,
        Comma,
        Equal,
        NotEqual,
        Greater,
        GreaterOrEqual,
        Less,
        LessOrEqual,
        In
    }

    // Represents a symbol in the constraint expression, which can be a number, variable name, operator, or delimiter.
    private readonly struct Symbol {
        public SymbolType Type { get; }
        public string Text { get; }
        public decimal Number { get; }

        // Initializes a new instance of the Symbol struct with the specified type, text, and optional numeric value.
        public Symbol(SymbolType type, string text, decimal number = 0) {
            Type = type;
            Text = text;
            Number = number;
        }
    }

    // Parses the constraint expression text into an expression tree that can be compiled into a function.
    private class Parser {
        private readonly List<Symbol> symbols;
        private int position;

        // Keeps track of the variables used in this constraint.
        private readonly HashSet<string> usedVariables =
            new HashSet<string>(StringComparer.Ordinal);

        // Makes the variables available outside the class.
        public IReadOnlyCollection<string> variables => usedVariables;

        // Represents the dictionary passed into the compiled function.
        private readonly ParameterExpression variableDictionary =
            Expression.Parameter(
                typeof(IReadOnlyDictionary<string, decimal>),
                "variables");

        // Converts the constraint text into symbols.
        public Parser(string text) {
            symbols = ConvertToSymbols(text);
        }

        // Builds the complete constraint expression.
        public Expression<Func<IReadOnlyDictionary<string, decimal>, bool>> BuildConstraint() {
            // All constraints are expected to be comparisons, so we start by building a comparison expression.
            Expression body = BuildComparison();

            // Makes sure there are no extra symbols left over.
            Expect(SymbolType.End);

            // Compiles the expression into a lambda function that takes a dictionary of variable assignments and returns a boolean indicating whether the constraint is satisfied.
            return Expression.Lambda<
                Func<IReadOnlyDictionary<string, decimal>, bool>>(body, variableDictionary);
        }


        // Builds the comparison expression, which can be either <, >, =, !=, <=, >= or a set membership check (IN).
        private Expression BuildComparison() {
            // Start by building the left-hand side of the comparison, which can be an arithmetic expression.
            Expression left = BuildArithmetic();

            // If the next symbol is IN, parse it as a membership check instead of a comparison.
            if (Current.Type == SymbolType.In) {
                return BuildSetMembership(left);
            }

            // If the next symbol is not a comparison operator, throw an error indicating that a comparison operator was expected.
            SymbolType op = Current.Type;
            if (!IsComparisonOperator(op)) {
                throw Error(
                    "Expected a comparison operator such as <, >, = or !=.");
            }

            // Advance to the next symbol, which is the right-hand side of the comparison.
            Advance();

            // Build the right-hand side of the comparison, which can also be an arithmetic expression.
            Expression right = BuildArithmetic();

            // Build the comparison expression based on the operator type, using the left and right expressions.
            if (op == SymbolType.Equal)
                return Expression.Equal(left, right);
            if (op == SymbolType.NotEqual)
                return Expression.NotEqual(left, right);
            if (op == SymbolType.Greater)
                return Expression.GreaterThan(left, right);
            if (op == SymbolType.GreaterOrEqual)
                return Expression.GreaterThanOrEqual(left, right);
            if (op == SymbolType.Less)
                return Expression.LessThan(left, right);
            if (op == SymbolType.LessOrEqual)
                return Expression.LessThanOrEqual(left, right);
            throw Error("Unsupported comparison operator.");
        }

        // membership:
        // value IN { value1, value2, ... }

        private Expression BuildSetMembership(Expression value) {
            // Expect the IN keyword followed by a left bracket, then parse the values inside the brackets as arithmetic expressions.
            Expect(SymbolType.In);
            Expect(SymbolType.LeftBracket);

            // Build a list of comparison expressions for each value in the set, checking if the left-hand side value is equal to any of the values in the set.
            var comparisons = new List<Expression>();

            // Read each element inside the braces.
            if (Current.Type != SymbolType.RightBracket) {
                while (true) {
                    // Build the arithmetic expression for the current item in the set.
                    Expression element = BuildArithmetic();

                    // Compare the left-hand side value with the current element using equality, and add the comparison to the list of comparisons.
                    Expression comparison = Expression.Equal(value, element);
                    comparisons.Add(comparison);

                    // Stop if there are no more items.
                    if (Current.Type != SymbolType.Comma) {
                        break;
                    }

                    // If there is a comma, advance to the next item in the set.
                    Advance();
                }
            }

            // Expect the closing right bracket to complete the set membership expression.
            Expect(SymbolType.RightBracket);

            // An empty set never contains a value.
            if (comparisons.Count == 0) {
                return Expression.Constant(false);
            }

            // Combine the comparisons using OR to check if the left-hand side value is equal to any of the values in the set.
            Expression result = comparisons[0];
            for (int i = 1; i < comparisons.Count; i++) {
                result = Expression.OrElse(result, comparisons[i]);
            }
            return result;
        }


        // Builds an arithmetic expression, which can include addition, subtraction, multiplication, division, unary operators, and parentheses.
        private Expression BuildArithmetic() {
            // Start by building the left-hand side of the arithmetic expression, which can be a multiplicative expression.
            Expression left = BuildMultiplication();

            // Handle addition and subtraction
            // Loops through to complete all addition and subtraction operations in the expression
            while (Current.Type == SymbolType.Plus || Current.Type == SymbolType.Minus) {

                // Advance to the next symbol, which is the operator for addition or subtraction.
                SymbolType op = Advance().Type;

                // Build the right-hand side of the arithmetic expression, which can also be multiplication or division.
                Expression right = BuildMultiplication();

                // Combine the left and right expressions based on the operator type, using addition or subtraction.
                if (op == SymbolType.Plus) {
                    left = Expression.Add(left, right);
                } else {
                    left = Expression.Subtract(left, right);
                }
            }
            return left;
        }

        // Builds expressions with multiplication and division

        private Expression BuildMultiplication() {
            // Start by building the left-hand side of the multiplication/division expression, which can be a single operand or a unary expression.
            Expression left = BuildUnary();

            // Loops through to complete all multiplication and division operations in the expression
            while (Current.Type == SymbolType.Multiply || Current.Type == SymbolType.Divide) {
                // Advance to the next symbol, which is the operator for multiplication or division.
                SymbolType op = Advance().Type;

                // Build the right-hand side of the multiplication/division expression, which can also be a single operand or a unary expression.
                Expression right = BuildUnary();

                // Combine the left and right expressions based on the operator type, using multiplication or division.
                if (op == SymbolType.Multiply) {
                    left = Expression.Multiply(left, right);
                } else {
                    left = Expression.Divide(left, right);
                }
            }
            return left;
        }

        // Builds unary expressions, which can include negation and positive signs.
        private Expression BuildUnary() {
            // If the current symbol is a minus sign, advance to the next symbol and build the unary expression for negation.
            if (Current.Type == SymbolType.Minus) {
                Advance();
                return Expression.Negate(BuildUnary());
            }
            // If the current symbol is a plus sign, advance to the next symbol and build the unary expression for positive sign (which does not change the value).
            if (Current.Type == SymbolType.Plus) {
                Advance();
                return BuildUnary();
            }

            // If the current symbol is not a unary operator, parse it as a primary expression.
            return BuildPrimary();
        }

        // Builds primary expressions, which can be numbers, variables, or parenthesized arithmetic expressions.
        private Expression BuildPrimary() {
            Symbol symbol = Current;

            // If the current symbol is a number, advance to the next symbol and return a constant expression representing the number.
            if (symbol.Type == SymbolType.Number) {
                Advance();
                return Expression.Constant(symbol.Number);
            }

            // If the current symbol is a variable name, advance to the next symbol and return an expression that retrieves the variable's value from the dictionary of variable assignments.
            if (symbol.Type == SymbolType.VariableName) {
                Advance();
                usedVariables.Add(symbol.Text);

                // Build an expression that accesses the variable from the dictionary
                return Expression.Property(
                    variableDictionary,
                    "Item",
                    Expression.Constant(symbol.Text));
            }

            // If the current symbol is a left parenthesis, advance to the next symbol, build the arithmetic expression inside the parentheses, and expect a right parenthesis to close the expression.
            if (symbol.Type == SymbolType.LeftParenthesis) {
                Advance();
                Expression expression = BuildArithmetic();
                Expect(SymbolType.RightParenthesis);
                return expression;
            }

            // If the current symbol is not a number, variable, or left parenthesis, throw an error indicating that a number, variable, or parenthesized expression was expected.
            throw Error("Expected a number, variable or parenthesized expression.");
        }

        // Checks if the given symbol type is a comparison operator (e.g., =, !=, <, >, <=, >=).
        private static bool IsComparisonOperator(SymbolType type) {
            return type == SymbolType.Equal ||
                   type == SymbolType.NotEqual ||
                   type == SymbolType.Greater ||
                   type == SymbolType.GreaterOrEqual ||
                   type == SymbolType.Less ||
                   type == SymbolType.LessOrEqual;
        }

        // Symbol navigation

        // Returns the current symbol in the list of symbols based on the current position.
        private Symbol Current => symbols[position];

        // Advances to the next symbol in the list and returns the current symbol before advancing.
        private Symbol Advance() {
            Symbol symbol = Current;
            if (Current.Type != SymbolType.End)
                position++;
            return symbol;
        }

        // Handles the expectation of a specific symbol type in the parsing process and then advances the position in the list of symbols.
        private Symbol Expect(SymbolType type) {
            // If the current symbol does not match the expected type, it throws a FormatException with an appropriate error message.
            if (Current.Type != type) {
                throw Error($"Expected {type}, found '{Current.Text}'.");
            }
            return Advance();
        }

        // Creates a FormatException with a message indicating the expected symbol and the actual symbol found.
        private FormatException Error(string message) {
            return new FormatException(
                $"{message} Found '{Current.Text}'.");
        }

        // Converts text into symbols for evaluation, including numbers, variable names, operators, and delimiters.
        private static List<Symbol> ConvertToSymbols(string text) {
            // Start with an empty list of symbols and an index at the beginning of the text.
            var symbols = new List<Symbol>();
            int i = 0;

            // Loop through the text until the end is reached.
            while (i < text.Length) {
                // Look at the current character in the text.
                char c = text[i];

                // Skip whitespace characters and move to the next character.
                if (char.IsWhiteSpace(c)) {
                    i++;
                    continue;
                }

                // If the character is a digit or a decimal point followed by a digit, parse it as a number.
                if (char.IsDigit(c) || (c == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1]))) {
                    // Start parsing the number from the current index.
                    int start = i;

                    // Parse the integer part of the number.
                    while (i < text.Length &&
                           char.IsDigit(text[i]))
                        i++;

                    // Parse the decimal part of the number if present.
                    if (i < text.Length && text[i] == '.') {
                        i++;

                        // Ensure that there is at least one digit after the decimal point, otherwise throw a FormatException indicating an invalid decimal number.
                        if (i >= text.Length ||
                            !char.IsDigit(text[i])) {
                            throw new FormatException(
                                "Invalid decimal number.");
                        }

                        // Parse the decimal part of the number.
                        while (i < text.Length &&
                               char.IsDigit(text[i]))
                            i++;
                    }

                    // Extract the substring representing the number. 
                    string numberText =
                        text.Substring(start, i - start);

                    // Try to parse the extracted number into a decimal value. If parsing fails, throw a FormatException indicating an invalid number.
                    if (!decimal.TryParse(
                        numberText,
                        NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture,
                        out decimal number)) {
                        throw new FormatException(
                            $"Invalid number '{numberText}'.");
                    }

                    // Add the parsed number as a symbol to the list of symbols.
                    symbols.Add(new Symbol(
                        SymbolType.Number,
                        numberText,
                        number));

                    continue;
                }

                // If the character is a letter or an underscore, parse it as a variable name or a reserved keyword (like "in").
                if (char.IsLetter(c) || c == '_') {
                    // Start parsing the text from the current index.
                    int start = i++;

                    // Continue parsing while the characters are letters, digits, or underscores.
                    // Digits are allowed after the first character, but not as the first character of a variable name.
                    while (i < text.Length &&
                           (char.IsLetterOrDigit(text[i]) ||
                            text[i] == '_'))
                        i++;

                    // Extract the substring representing the variable name or keyword.
                    string name = text.Substring(start, i - start);

                    // Determine the type of symbol based on the extracted name. 
                    // If the name is "in" (case-insensitive), it is treated as a reserved keyword; otherwise, it is treated as a variable name.
                    SymbolType type;
                    if (name.Equals("in", StringComparison.OrdinalIgnoreCase)) {
                        type = SymbolType.In;
                    } else {
                        type = SymbolType.VariableName;
                    }

                    // Add the parsed variable name or keyword as a symbol to the list of symbols.
                    symbols.Add(new Symbol(type, name));

                    continue;
                }

                // Parse operators and delimiters based on the current character, such as +, -, *, /, (, ), {, }, ,, =, !=, >, >=, <, <=.
                switch (c) {
                    case '+':
                        symbols.Add(new Symbol(SymbolType.Plus, "+"));
                        i++;
                        break;
                    case '-':
                        symbols.Add(new Symbol(SymbolType.Minus, "-"));
                        i++;
                        break;
                    case '*':
                        symbols.Add(new Symbol(SymbolType.Multiply, "*"));
                        i++;
                        break;
                    case '/':
                        symbols.Add(new Symbol(SymbolType.Divide, "/"));
                        i++;
                        break;
                    case '(':
                        symbols.Add(new Symbol(SymbolType.LeftParenthesis, "("));
                        i++;
                        break;
                    case ')':
                        symbols.Add(new Symbol(SymbolType.RightParenthesis, ")"));
                        i++;
                        break;
                    case '{':
                        symbols.Add(new Symbol(SymbolType.LeftBracket, "{"));
                        i++;
                        break;
                    case '}':
                        symbols.Add(new Symbol(SymbolType.RightBracket, "}"));
                        i++;
                        break;
                    case ',':
                        symbols.Add(new Symbol(SymbolType.Comma, ","));
                        i++;
                        break;
                    case '=':
                        i++;
                        // If the next character is also '=', it accepts '==' but treats it as a single '=' symbol for checking equality.
                        if (i < text.Length && text[i] == '=')
                            i++;
                        symbols.Add(new Symbol(SymbolType.Equal, "="));
                        break;
                    case '!':
                        i++;
                        // If the next character is not '=', it throws a FormatException indicating that '!=' was expected.
                        if (i >= text.Length || text[i] != '=')
                            throw new FormatException(
                                "Expected '!='.");
                        i++;
                        symbols.Add(new Symbol(SymbolType.NotEqual, "!="));
                        break;
                    case '>':
                        i++;
                        // If the next character is '=', it treats it as a 'greater than or equal to' symbol; otherwise, it treats it as a 'greater than' symbol.
                        if (i < text.Length && text[i] == '=') {
                            i++;
                            symbols.Add(new Symbol(SymbolType.GreaterOrEqual, ">="));
                        } else {
                            symbols.Add(new Symbol(SymbolType.Greater, ">"));
                        }
                        break;
                    case '<':
                        i++;
                        // If the next character is '=', it treats it as a 'less than or equal to' symbol; otherwise, it treats it as a 'less than' symbol.
                        if (i < text.Length && text[i] == '=') {
                            i++;
                            symbols.Add(new Symbol(SymbolType.LessOrEqual, "<="));
                        } else {
                            symbols.Add(new Symbol(SymbolType.Less, "<"));
                        }
                        break;
                    // If the character does not match any of the expected symbols, it throws a FormatException indicating an unexpected character at the current position.
                    default:
                        throw new FormatException(
                            $"Unexpected character '{c}' at position {i}.");
                }
            }
            // Add an end symbol to indicate the end of the input text.
            symbols.Add(new Symbol(SymbolType.End, ""));
            return symbols;
        }
    }
}
