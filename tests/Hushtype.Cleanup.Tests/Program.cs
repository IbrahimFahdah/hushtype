using Hushtype.Cleanup;

var cases = new (string Input, string Expected)[]
{
    ("testing one two three", "Testing one two three."),
    ("um, uh testing testing one", "Testing one."),
    ("you know, this works", "This works."),
    ("Do you know the answer?", "Do you know the answer?"),
    ("hello   world !", "Hello world!"),
    ("how are you ?", "How are you?"),
    ("wait , what", "Wait, what."),
    ("", ""), ("   ", ""), ("um uh erm", ""), ("um, uh.", ""), ("...", ""),
    ("umbrella and humble", "Umbrella and humble."),
    ("word WORD word", "Word."),
    ("dial 1 1 2 now", "Dial 1 1 2 now."),
    ("my PIN is 4 4 7 2", "My PIN is 4 4 7 2."),
    ("I had had enough", "I had had enough."),
    ("that that is fine", "That that is fine.")
};
foreach (var (input, expected) in cases)
{
    var actual = RuleBasedPostProcessor.Clean(input);
    if (actual != expected) throw new Exception($"For '{input}': expected '{expected}', got '{actual}'");
}
Console.WriteLine($"PASS: {cases.Length} cleanup cases.");
