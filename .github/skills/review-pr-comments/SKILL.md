---
name: review-pr-comments
description: >-
  Review and address all pull-request feedback for the PR associated with the
  active Git branch using authenticated GitHub CLI and broad GitHub GraphQL
  queries. Use whenever the user asks to review the current PR, address PR or
  review comments, clear review threads, resolve outdated conversations, or
  fix requested changes. Inventory hidden, outdated, resolved, and active
  threads; fix relevant issues one at a time; wait for approval to commit and
  push or push; then immediately explain and resolve the related PR threads.
compatibility: Requires git and an authenticated GitHub CLI (gh) session with access to the target repository.
---

# Review Pull Request and address comments

Use the `gh` CLI and GitHub GraphQL service to review the pull request
associated with the active branch. Always search broadly because GitHub can
collapse, hide, or filter outdated conversations.

Read [GitHub GraphQL operations](references/github-graphql.md) before querying
or mutating review threads.

## Stepwise instructions

1. For every comment you resolve, first add a note to its review thread that
   explains specifically why it is being resolved. Confirm that GitHub created
   the note before resolving the thread.
2. Mark all unresolved comments reported as `isOutdated: true` as resolved,
   after checking the full thread and current code and adding the required
   explanatory note.
3. Find active and relevant comments and review them one at a time. Prepare and
   validate the complete fix for each relevant comment; after the fix is
   pushed, add the explanatory note and mark its thread resolved.
4. While working on a fix, use the relevant comment as a type or example of
   what else may need the same fix. Keep that expanded search bounded to the
   same behavior, API contract, security issue, naming rule, or maintainability
   concern.
5. After fixing relevant issues, show the user the completed fixes,
   validations, and planned thread dispositions. Wait for permission to commit
   and push the fixes, or to push fixes that are already committed. Upon a
   successful push, immediately reply to and resolve the PR threads related to
   those fixes. The push approval covers those related replies and resolutions;
   do not pause for another approval after pushing.
6. Add a defensible no-change or duplicate note and mark all remaining
   unresolved review threads as resolved when no fix will be made. Leave a
   thread unresolved only when a concrete blocker prevents an honest
   disposition, and report that blocker.

## Discover and inventory the PR

1. Read the applicable repository policy and inspect the active branch,
   remotes, and working tree. Preserve unrelated local changes and never reset,
   clean, stash, amend, or discard them without explicit authorization.
2. Verify GitHub authentication and repository identity:

   ```powershell
   gh auth status -h github.com
   gh repo view --json nameWithOwner,url
   gh pr view --json number,url,title,state,headRefName,baseRefName
   ```

3. Stop without mutating GitHub if the active branch has no associated PR or a
   supplied PR URL does not match the branch-associated PR.
4. Use the GraphQL reference to paginate all of these separately:

   - Review threads, including resolved and unresolved, outdated state, path,
     line context, and every comment in each thread.
   - Top-level PR conversation comments.
   - Submitted reviews and their bodies, including suppressed findings.

5. If any connection reports `hasNextPage: true`, continue until it is false.
   If a thread has more than 100 comments, paginate that thread's comments
   separately.

Do not rely only on `gh pr view`, the Conversation tab, unresolved filters,
REST review comments, or the latest review.

## Build the working ledger

Record one row for every review thread with:

- Thread ID and URL.
- Path and line context.
- Author and full substantive conversation.
- `isResolved` and `isOutdated`.
- Classification: already resolved, outdated, active/relevant, no-change,
  duplicate, or blocked.
- Planned fix or rationale, resolution note, and verification result.

Keep top-level comments and submitted review summaries in a separate context
list. They can reveal additional work, but GitHub does not allow them to be
resolved with `resolveReviewThread`; never claim that they were resolved.

## Prepare one round of fixes

Process unresolved threads one at a time:

1. Read the entire thread and inspect the current code, nearby tests, and
   applicable repository instructions.
2. For outdated feedback, verify why it no longer applies and draft a concrete
   outdated-resolution note.
3. For active relevant feedback, search for bounded analogous occurrences,
   implement the complete fix, and add or update tests when practical.
4. Run the narrowest useful checks first, then broader checks proportional to
   the risk. Review the diff for unrelated changes and secrets.
5. Draft the exact reply that will be posted after the push. Use one of these
   dispositions and include relevant validation:

   - `Fixed`: what changed and how it was validated.
   - `Outdated`: what replaced the referenced code or behavior.
   - `No change`: why the current behavior is intentional or the feedback is
     duplicate.

6. For every remaining unresolved thread, prepare an honest disposition rather
   than silently leaving it open.

Do not post replies or resolve threads during preparation. Do not manufacture
agreement when a finding is blocked or unsafe to address.

## Approval checkpoint

Before committing or pushing, show the user:

- Files and behaviors changed, including bounded analogous fixes.
- Tests and checks run, including failures.
- Planned commit scope and message, or the commits that only need pushing.
- Every unresolved thread's classification, planned reply, and resolution.
- Any thread that must remain unresolved and its blocker.

Ask for permission to commit and push, or just push, as appropriate. The
initial request to review comments is not push approval. Until the user grants
that permission, do not stage, commit, push, post replies, or resolve threads.

## Complete the approved round

Once approval is given, treat the fix push and related thread cleanup as one
continuous operation:

1. Recheck the worktree and diff for unexpected changes.
2. Stage only the review-fix files or hunks, commit under repository policy when
   needed, and push without force to the PR branch.
3. If commit or push fails, do not mutate PR comments. Report the failure and
   retry only when safe or directed.
4. Immediately after a successful push, process each approved unresolved thread
   one at a time, outdated first:
   1. Re-read the thread and confirm its disposition still matches the pushed
      code.
   2. Add the prepared note with `addPullRequestReviewThreadReply`.
   3. Confirm the reply was created.
   4. Resolve with `resolveReviewThread`.
   5. Re-query the thread and verify `isResolved: true` and the new note.
5. Then process fixed active threads, followed by no-change and duplicate
   threads, using the same reply-before-resolution sequence.

Do not end the turn after a successful fix push while a thread covered by that
round remains unresolved. A GitHub mutation failure or newly discovered blocker
is the only reason to stop; identify the exact affected thread. If a new thread
arrives after the push, finish the approved round first and then begin a new
inventory-and-fix round for the new feedback.

When a round has no code changes, still obtain explicit approval before posting
replies and resolving its threads; state that comment updates are the only
proposed mutations.

## Reconcile and report

Run the full paginated GraphQL inventory again and verify:

- Every newly resolved thread has a newly created explanatory reply.
- Every unresolved outdated thread was processed.
- Every active relevant thread was fixed or explicitly dispositioned.
- Hidden and older-diff threads were not missed.
- Every thread still unresolved has a concrete reported blocker.
- Top-level comments and review summaries are reported separately.

Do not approve, merge, close, or submit a formal PR review unless the user also
requested that action.

Report the PR URL and number; counts for already-resolved,
outdated-resolved, fixed-and-resolved, no-change-resolved, and still-unresolved
threads; fixes and bounded analogous cleanup; validation results; commit hash;
non-resolvable context that still needs a response; blockers; and whether the
full approved round (commit, push, replies, resolutions, and verification)
completed.


