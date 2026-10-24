// GBFileCopier Finder extension: adds GBFileCopier to the Finder's context menu.
//
// App extensions run in a sandbox and can't start processes, so the extension talks to the app
// through its URL scheme:
//   gbfilecopier://add?source=/path&source=/path      the selected items become the sources
//   gbfilecopier://paste?destination=/folder&move=1   paste the clipboard files into the folder
// The app only fills in the window: the user still presses Copy or Move.
//
// Built without Xcode by packaging/macos/build.sh (swiftc, entry point NSExtensionMain).

import Cocoa
import FinderSync

@objc(GBFileCopierFinderSync)
final class GBFileCopierFinderSync: FIFinderSync {
    override init() {
        super.init()
        // Every folder, external and network volumes included
        FIFinderSyncController.default().directoryURLs = [URL(fileURLWithPath: "/")]
    }

    override func menu(for menuKind: FIMenuKind) -> NSMenu? {
        let menu = NSMenu(title: "")
        switch menuKind {
        case .contextualMenuForItems:
            menu.addItem(item("CopyOrMove", #selector(addSelection(_:))))
        case .contextualMenuForContainer:
            // Only when there are files to paste: copied in the Finder with ⌘C
            guard NSPasteboard.general.types?.contains(.fileURL) == true else { return nil }
            menu.addItem(item("PasteHere", #selector(pasteHere(_:))))
            menu.addItem(item("MoveHere", #selector(moveHere(_:))))
        default:
            return nil
        }
        return menu
    }

    @objc func addSelection(_ sender: AnyObject?) {
        guard let items = FIFinderSyncController.default().selectedItemURLs(), !items.isEmpty else { return }
        openApp("add", items.map { URLQueryItem(name: "source", value: $0.path) })
    }

    @objc func pasteHere(_ sender: AnyObject?) { paste(move: false) }

    @objc func moveHere(_ sender: AnyObject?) { paste(move: true) }

    private func paste(move: Bool) {
        guard let folder = FIFinderSyncController.default().targetedURL() else { return }
        openApp("paste", [URLQueryItem(name: "destination", value: folder.path), URLQueryItem(name: "move", value: move ? "1" : "0")])
    }

    private func openApp(_ action: String, _ queryItems: [URLQueryItem]) {
        var components = URLComponents()
        components.scheme = "gbfilecopier"
        components.host = action
        components.queryItems = queryItems
        if let url = components.url {
            NSWorkspace.shared.open(url)
        }
    }

    private func item(_ key: String, _ action: Selector) -> NSMenuItem {
        let title = Bundle(for: GBFileCopierFinderSync.self).localizedString(forKey: key, value: nil, table: nil)
        let item = NSMenuItem(title: title, action: action, keyEquivalent: "")
        item.image = NSImage(named: "MenuIcon")
        return item
    }
}
