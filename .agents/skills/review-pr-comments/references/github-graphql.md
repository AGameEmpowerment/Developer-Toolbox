## GitHub GraphQL operations

Use these operations through `gh api graphql`. Supply `owner`, `name`, and
`number` from `gh repo view` and the branch-associated `gh pr view`; never
hard-code another pull request.

Adapt quoting and variable assignment to the active shell. Use `-F` for typed
GraphQL variables such as the PR number and `-f` for strings. Keep response
payloads out of source control when they contain private repository content.

## Review-thread inventory

Paginate `reviewThreads` until `hasNextPage` is false. With `gh api graphql
--paginate`, the query must accept `$endCursor` and return `pageInfo`.

```graphql
query ReviewThreads(
  $owner: String!
  $name: String!
  $number: Int!
  $endCursor: String
) {
  repository(owner: $owner, name: $name) {
    pullRequest(number: $number) {
      id
      number
      url
      reviewThreads(first: 100, after: $endCursor) {
        nodes {
          id
          isResolved
          isOutdated
          path
          line
          originalLine
          diffSide
          comments(first: 100) {
            nodes {
              id
              databaseId
              url
              body
              createdAt
              lastEditedAt
              author {
                login
              }
              replyTo {
                id
              }
            }
            pageInfo {
              hasNextPage
              endCursor
            }
          }
        }
        pageInfo {
          hasNextPage
          endCursor
        }
      }
    }
  }
}
```
If a thread contains more than 100 comments, run a focused follow-up query for
that thread's comments with their own cursor. Do not assume the first 100 are
the complete conversation.

## Top-level conversation comments

Paginate the PR `comments` connection separately so its cursor does not compete
with the review-thread cursor.

```graphql
query PullRequestComments(
  $owner: String!
  $name: String!
  $number: Int!
  $endCursor: String
) {
  repository(owner: $owner, name: $name) {
    pullRequest(number: $number) {
      comments(first: 100, after: $endCursor) {
        nodes {
          id
          databaseId
          url
          body
          createdAt
          lastEditedAt
          isMinimized
          minimizedReason
          author {
            login
          }
        }
        pageInfo {
          hasNextPage
          endCursor
        }
      }
    }
  }
}
```

These comments provide review context but cannot be resolved with
`resolveReviewThread`.

## Submitted reviews

Paginate reviews separately and inspect review bodies even when the review is
old or dismissed.

```graphql
query PullRequestReviews(
  $owner: String!
  $name: String!
  $number: Int!
  $endCursor: String
) {
  repository(owner: $owner, name: $name) {
    pullRequest(number: $number) {
      reviews(first: 100, after: $endCursor) {
        nodes {
          id
          databaseId
          url
          body
          state
          submittedAt
          author {
            login
          }
        }
        pageInfo {
          hasNextPage
          endCursor
        }
      }
    }
  }
}
```

## Add the mandatory resolution note

Run this mutation first for every thread that will be resolved. Capture the
returned comment ID and URL as proof that the note was created.

```graphql
mutation AddResolutionNote($threadId: ID!, $body: String!) {
  addPullRequestReviewThreadReply(
    input: {
      pullRequestReviewThreadId: $threadId
      body: $body
    }
  ) {
    comment {
      id
      url
      body
    }
  }
}
```

If this mutation fails, do not resolve the thread.

## Resolve the review thread

Run only after the resolution note succeeds.

```graphql
mutation ResolveReviewThread($threadId: ID!) {
  resolveReviewThread(input: { threadId: $threadId }) {
    thread {
      id
      isResolved
      isOutdated
    }
  }
}
```

Re-query the thread after the mutation. Count it as resolved only when GitHub
returns `isResolved: true`.

## Focused verification query

Use the thread node ID to verify its final state and newest replies.

```graphql
query VerifyReviewThread($threadId: ID!) {
  node(id: $threadId) {
    ... on PullRequestReviewThread {
      id
      isResolved
      isOutdated
      comments(last: 10) {
        nodes {
          id
          url
          body
          createdAt
          author {
            login
          }
        }
      }
    }
  }
}
```
