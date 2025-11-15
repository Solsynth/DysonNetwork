# Client-Side Guide: Handling the New Message Structure

This document outlines how to update your client application to support the new rich message structure for the thinking/chat feature. The backend now sends structured messages that can include plain text, function calls, and function results, allowing for a more interactive and transparent user experience.

When using with gateway, all the response type are in snake case

## 1. Data Models

When you receive a complete message (a "thought"), it will be in the form of an `SnThinkingThought` object. The core of this object is the `Parts` array, which contains the different components of the message.

Here are the primary data models you will be working with, represented here in a TypeScript-like format for clarity:

```typescript
// The main message object from the assistant or user
interface SnThinkingThought {
  id: string;
  parts: SnThinkingMessagePart[];
  role: 'Assistant' /*Value is (0)*/ | 'User' /*Value is (1)*/;
  createdAt: string; // ISO 8601 date string
  // ... other metadata
}

// A single part of a message
interface SnThinkingMessagePart {
  type: ThinkingMessagePartType;
  text?: string;
  functionCall?: SnFunctionCall;
  functionResult?: SnFunctionResult;
}

// Enum for the different part types
enum ThinkingMessagePartType {
  Text = 0,
  FunctionCall = 1,
  FunctionResult = 2,
}

// Represents a function/tool call made by the assistant
interface SnFunctionCall {
  id: string;
  name: string;
  arguments: string; // A JSON string of the arguments
}

// Represents the result of a function call
interface SnFunctionResult {
  callId: string;      // The ID of the corresponding function call
  result: any;         // The data returned by the function
  isError: boolean;
}
```

## 2. Handling the SSE Stream

The response is streamed using Server-Sent Events (SSE). Your client should listen to this stream and process events as they arrive to build the UI in real-time.

The stream sends different types of messages, identified by a `type` field in the JSON payload.

| Event Type               | `data` Payload                               | Client-Side Action                                                                                                                            |
| ------------------------ | -------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| `text`                   | `{ "type": "text", "data": "some text" }`    | Append the text content to the current message being displayed. This is the most common event.                                                |
| `function_call_update`   | `{ "type": "function_call_update", "data": { ... } }` | This provides real-time updates as the AI decides on a function call. You can use this to show an advanced "thinking" state, but it's optional. The key events to handle are `function_call` and `function_result`. |
| `function_call`          | `{ "type": "function_call", "data": SnFunctionCall }` | The AI has committed to using a tool. Display a "Using tool..." indicator. You can show the `name` of the tool for more clarity. |
| `function_result`        | `{ "type": "function_result", "data": SnFunctionResult }` | The tool has finished running. You can hide the "thinking" indicator for this tool and optionally display a summary of the result. |
| `topic`                  | `{ "type": "topic", "data": "A new topic" }` | If this is the first message in a new conversation, this event provides the auto-generated topic title. Update your UI accordingly. |
| `thought`                | `{ "type": "thought", "data": SnThinkingThought }` | This is the **final event** in the stream. It contains the complete, persisted message object with all its `Parts`. You should use this final object to replace the incrementally-built message in your state to ensure consistency. |

## 3. Rendering a Message from `SnThinkingThought`

Once you have the final `SnThinkingThought` object (either from the `thought` event in the stream or by fetching conversation history), you can render it by iterating through the `parts` array.

### Pseudocode for Rendering

```javascript
function renderThought(thought: SnThinkingThought) {
  const messageContainer = document.createElement('div');
  messageContainer.className = `message message-role-${thought.role}`;

  // User messages are simple and will only have one text part
  if (thought.role === 'User') {
    const textPart = thought.parts[0];
    messageContainer.innerText = textPart.text;
    return messageContainer;
  }

  // Assistant messages can have multiple parts
  let textBuffer = '';

  thought.parts.forEach(part => {
    switch (part.type) {
      case ThinkingMessagePartType.Text:
        // Buffer text to combine consecutive text parts
        textBuffer += part.text;
        break;

      case ThinkingMessagePartType.FunctionCall:
        // First, render any buffered text
        if (textBuffer) {
          messageContainer.appendChild(renderText(textBuffer));
          textBuffer = '';
        }
        // Then, render the function call UI component
        messageContainer.appendChild(renderFunctionCall(part.functionCall));
        break;

      case ThinkingMessagePartType.FunctionResult:
        // Render buffered text
        if (textBuffer) {
          messageContainer.appendChild(renderText(textBuffer));
          textBuffer = '';
        }
        // Then, render the function result UI component
        messageContainer.appendChild(renderFunctionResult(part.functionResult));
        break;
    }
  });

  // Render any remaining text at the end
  if (textBuffer) {
    messageContainer.appendChild(renderText(textBuffer));
  }

  return messageContainer;
}

// Helper functions to create UI components
function renderText(text) {
  const p = document.createElement('p');
  p.innerText = text;
  return p;
}

function renderFunctionCall(functionCall) {
  const el = document.createElement('div');
  el.className = 'function-call-indicator';
  el.innerHTML = `<i>Using tool: <strong>${functionCall.name}</strong>...</i>`;
  // You could add a button to show functionCall.arguments
  return el;
}

function renderFunctionResult(functionResult) {
  const el = document.createElement('div');
  el.className = 'function-result-indicator';
  if (functionResult.isError) {
    el.classList.add('error');
    el.innerText = 'An error occurred while using the tool.';
  } else {
    el.innerText = 'Tool finished.';
  }
  // You could expand this to show a summary of functionResult.result
  return el;
}
```

This approach ensures that text and tool-use indicators are rendered inline and in the correct order, providing a clear and accurate representation of the assistant's actions.
