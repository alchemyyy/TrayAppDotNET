# Other Pages

App history, Startup apps, Users and Services: the rest of the Windows 11 Task Manager pages. They're simpler than [Processes](processes.md) and all work the same way, so the shared bits come first.

## What They Share

Each page has a search box at the top of the window, a header with the page's buttons, and a table.

- **Search** filters as you type. It matches any row that contains the text in any column, ignoring case. No expressions here; those are only on the Processes page.
- **Run new task** is on every page and works exactly like on Processes: type an executable, document or URI, then ++enter++ or **Run**. See [Run New Task](processes.md#run-new-task).
- **More** (the **...** button) opens the page's menu. It always has **Refresh**, plus the page's actions.
- Click a column header to sort by it, click again to reverse. The first column (name) is the default sort.
- Drag a divider in the header to resize a column. The widths go back to their defaults when the app restarts.
- When there's nothing to show, the table says **No items to display.**
- [Zoom and stretch](processes.md#zoom-and-stretch) work in these tables too, with the same gestures, and share the same settings.

Unlike the Processes table, these tables have keyboard navigation:

| Input | Action |
| --- | --- |
| ++up++ / ++down++ | Select the previous / next row |
| ++home++ / ++end++ | Select the first / last row |
| ++left++ / ++right++ | Collapse / expand the selected group row (Users) |
| ++enter++ or double-click | Run the row's default action (see each page) |

**Note**: these keys don't scroll the selected row into view. If you arrow past the bottom of the visible area, scroll to find it.

Like the Processes page, a page only does work while it's showing and the window is visible and not minimized.

## App History

Per-app CPU time and network use, collected by TADN's Task Manager itself. The line above the table says since when, e.g. **Resource usage collected by TaskManagerTrayAppDotNET since** *date and time* **for current user and system accounts.**

| Column | What it shows |
| --- | --- |
| **Name** | The program. One row per executable, however many processes it ran. |
| **CPU time** | CPU time used since collection started, `H:MM:SS`, or `1d 8:10:22` past a day |
| **Network** | Network traffic, in MB |
| **Notifications** | Always empty, see below |

This isn't Windows' own app history; TADN collects its own. What you see here is:

- Only collected while the App history page is on screen. CPU time catches up for processes that are still running when you come back, but processes that start and exit while you're on another page are never seen.
- Kept in memory only. Nothing is written to disk, so it starts over when the app restarts.
- **Network** is estimated from the sampled per-process rate, so it inherits the [Network column's](processes.md#resource-columns) limits: without elevation, only your own processes count.
- **Notifications** is there to match Windows Task Manager, but TADN doesn't read notification counts, so it stays empty.

**Delete usage history** (the link under the description, or **More** > **Delete usage history**) clears everything immediately, without asking, and restarts the collection from now. Row activation does nothing on this page.

## Startup Apps

Programs Windows starts when you sign in, and whether they're enabled.

TADN reads them from:

- The `Run` and `RunOnce` registry keys, for your user (`HKCU`) and for all users (`HKLM`), in both the 64-bit and 32-bit registry views.
- Your Startup folder and the all-users Startup folder.

| Column | What it shows |
| --- | --- |
| **Name** | The entry's name |
| **Publisher** | Company name from the program's file properties, blank if it has none |
| **Status** | **Enabled**, **Disabled**, or **Unknown** when the state can't be read |
| **Startup impact** | Always **Not measured**. TADN doesn't measure startup impact. |

Disabled entries are drawn dimmed.

Buttons and **More** menu:

- **Enable** / **Disable**: flips the entry's `StartupApproved` flag, the same switch Windows uses. The registration itself stays, so you can turn it back on any time. Only the one that applies is enabled.
- **Properties**: opens the Windows file properties of the program. Only available when the program file could be found. ++enter++ and double-click do the same.

Entries registered for all users need administrator approval to change: you get the **Administrator approval required** prompt and then a UAC prompt. Entries for your own user change right away. See [Task Manager](index.md) for how the elevated helper works.

***This page doesn't refresh on its own.*** It reads the list when you open the page and after each change. If something adds or removes a startup entry while you're looking at it, use **More** > **Refresh**.

## Users

Who's signed in to this PC and what their processes are using.

Each signed-in session is a group row, named after the account with the number of processes in brackets. Expand it (click the chevron, double-click, ++enter++ or ++right++) to see the processes.

| Column | User row | Process row |
| --- | --- | --- |
| **User** | Account name and process count | Process name |
| **Status** | **Active**, **Connected**, **Disconnected** or **Idle** | Empty |
| **CPU** | Sum of the session's processes | The process's CPU |
| **Memory** | Sum of working sets, in MB | Working set, in MB |
| **Disk** | Sum, in MB/s | Disk rate, in MB/s |
| **Network** | Sum, in Mbps | Network rate, in Mbps |

The headers carry live totals: **CPU** and **Memory** show the system-wide usage percentage (e.g. `12.3% CPU`), **Disk** and **Network** the sum of all sessions. Disk and network cells are blank when the value can't be read. The page updates about once a second, together with the process list.

Buttons and **More** menu:

- **Disconnect**: disconnects the selected user's session. Select the user row, not a process. Only available for **Active**, **Connected** and **Idle** sessions. It asks first: **Do you want to disconnect** *DOMAIN\user*? and the user's programs keep running so they can sign back in. On your own session the prompt says so (**Do you want to disconnect your own session** ...?), since that sends you to the sign-in screen. Confirm with **Disconnect user**; **Cancel** (or ++esc++) leaves the session alone. Disconnecting someone else's session can need administrator approval; you'll get the prompt when Windows refuses.
- **Manage user accounts**: opens **Settings** > **Accounts** > **Other users** in Windows Settings.

## Services

Every Windows service on this PC, refreshed every 3 seconds while the page is showing.

| Column | What it shows |
| --- | --- |
| **Name** | The service's short name, e.g. `wuauserv` |
| **PID** | Process ID of a running service. Blank when stopped. |
| **Description** | The service's description text |
| **Status** | **Running**, **Stopped**, **Starting**, **Stopping**, **Paused**, **Pausing**, **Continuing** or **Unknown** |
| **Group** | The service host group, for services that run inside `svchost.exe` |

**Note**: **Name** is the short service name, not the friendly display name you see in the Services console. Search also matches the **Description** column.

Buttons and **More** menu. Each is only enabled (or listed in **More**) when it makes sense for the selected service:

| Action | Available when | What it does |
| --- | --- | --- |
| **Start** | Stopped and not disabled | Starts the service. ++enter++ and double-click do the same. |
| **Stop** | Running or paused, and the service accepts stop | Stops the service |
| **Restart** | Running or paused, accepts stop, not disabled | Stops it, then starts it again |
| **Disable** | Not disabled, and not in the middle of starting or stopping | Sets the startup type to Disabled, after a confirmation |
| **Open Services** | Always | Opens the Windows Services console (`services.msc`) |

- Start, Stop and Restart wait up to 30 seconds for the service to get there. If it doesn't, you get an error.
- **Disable** asks **Disable service?** first. It only changes the startup type: a running service keeps running until you stop it or reboot. To undo it, set the startup type back in the Services console; there's no **Enable** here.
- When Windows refuses an action for lack of rights, you get the **Administrator approval required** prompt and then a UAC prompt. Approve once and later actions go through without asking. See [Task Manager](index.md).
- **More** > **Refresh** also re-reads each service's description and group, which are otherwise read once and cached.
