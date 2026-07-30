# Question Bank CSV Generation Prompt

Copy the prompt below and replace the bracketed values.

~~~text
Generate [NUMBER] assessment questions for the question group "[GROUP TITLE]".

Purpose: [DESCRIBE WHAT THE TEST SHOULD ASSESS]
Audience: [DESCRIBE THE TARGET USERS]
Difficulty range: [FOR EXAMPLE: 1-3]
Language: [LANGUAGE]
Category identifiers: [FOR EXAMPLE: platform, security, networking]

Return CSV only. Do not include Markdown fences, explanations, headings, or blank lines.

Use this exact header:
content,category,type,grading_mode,difficulty,points,choices,correct_choices

Rules:
- Each row is one question.
- category must be one of the supplied category identifiers. Use lowercase kebab-case identifiers such as `networking` or `platform-basics`.
- Distribute questions as evenly as possible across the supplied categories. This allows test shuffling to select a balanced mix.
- If no category identifiers are supplied, leave category empty for every row.
- type must be one of: single_choice, multiple_choice, free_text.
- grading_mode must be auto or manual.
- Use auto only for choice questions with at least one correct answer.
- Use manual for free_text questions. Leave choices and correct_choices empty for free_text.
- difficulty must be a whole number from 1 to 5.
- points must be a positive number.
- In choices, separate choices with |. Do not use | inside a choice.
- In correct_choices, provide zero-based choice indexes separated with |. For example, if the second choice is correct, use 1.
- single_choice must have exactly one correct choice index.
- multiple_choice may have multiple correct choice indexes.
- Quote any CSV field that contains a comma, quote, or newline. Escape quotes inside quoted fields by doubling them.
- Keep questions factual, unambiguous, and suitable for automatic grading.
- Do not repeat questions or make "all of the above" / "none of the above" choices.

Example:
content,category,type,grading_mode,difficulty,points,choices,correct_choices
What is 2+2?,math,single_choice,auto,1,1,3|4|5,1
Which are prime numbers?,math,multiple_choice,auto,2,2,2|3|4|5,0|1|3
Explain why version control is useful.,workflow,free_text,manual,2,3,,
~~~
