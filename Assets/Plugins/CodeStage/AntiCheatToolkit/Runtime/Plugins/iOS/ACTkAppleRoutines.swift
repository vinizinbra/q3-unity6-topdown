//
//  ACTkAppleRoutines.swift
//  Anti-Cheat Toolkit
//
//  Copyright (C) Dmitry Yuhanov [https://codestage.net]
//
//  Native helper that resolves the app installation source on iOS / iPadOS for
//  CodeStage.AntiCheat.Genuine.Apple. The MarketplaceKit AppDistributor and StoreKit
//  AppTransaction APIs are async, so the result is resolved once at launch on a detached
//  Task and cached; the C entry points return the cached value (Pending until resolved).
//  Symbols are consumed from C# via [DllImport("__Internal")] in AppleRoutines.cs.
//
//  NOTE: do not obfuscate / rename the exported ACTk_AppleSource_* symbols.
//

import Foundation
import StoreKit
#if canImport(MarketplaceKit)
import MarketplaceKit
#endif

// Distributor kinds returned to C#. Keep in sync with AppleAppInstallationSource.cs.
private let kDistributorAppStore: Int32 = 0
private let kDistributorTestFlight: Int32 = 1
private let kDistributorMarketplace: Int32 = 2
private let kDistributorWeb: Int32 = 3
private let kDistributorOther: Int32 = 4
private let kDistributorUnavailable: Int32 = 5
private let kDistributorPending: Int32 = 6

// Environment kinds returned to C#. Keep in sync with AppleAppInstallationSource.cs.
private let kEnvironmentProduction: Int32 = 0
private let kEnvironmentSandbox: Int32 = 1
private let kEnvironmentXcode: Int32 = 2
private let kEnvironmentUnavailable: Int32 = 3
private let kEnvironmentPending: Int32 = 4
private let kEnvironmentError: Int32 = 5

private final class ACTkAppleSourceCache {
    static let shared = ACTkAppleSourceCache()

    private let lock = NSLock()
    private var distributorKind: Int32 = kDistributorPending
    private var environmentKind: Int32 = kEnvironmentPending
    private var marketplaceBundleId: UnsafeMutablePointer<CChar>?
    private var resolved = false
    private var started = false

    func start() {
        lock.lock()
        let alreadyStarted = started
        started = true
        lock.unlock()
        if alreadyStarted { return }

        if #available(iOS 13.0, *) {
            Task.detached { [weak self] in
                await self?.resolve()
            }
        } else {
            // Swift concurrency requires iOS 13+. Below it there is no AppDistributor (17.4) or
            // AppTransaction (16), so mark both unavailable and resolved; detection then falls back to
            // the synchronous receipt-name heuristic.
            setDistributor(kDistributorUnavailable, bundleId: nil)
            setEnvironment(kEnvironmentUnavailable)
            markResolved()
        }
    }

    func getDistributorKind() -> Int32 {
        lock.lock(); defer { lock.unlock() }
        return distributorKind
    }

    func getEnvironmentKind() -> Int32 {
        lock.lock(); defer { lock.unlock() }
        return environmentKind
    }

    func isResolved() -> Bool {
        lock.lock(); defer { lock.unlock() }
        return resolved
    }

    // Returns a cached C string pointer (allocated once for the app lifetime). The caller must NOT free it.
    func getMarketplaceBundleId() -> UnsafePointer<CChar>? {
        lock.lock(); defer { lock.unlock() }
        if let pointer = marketplaceBundleId {
            return UnsafePointer(pointer)
        }
        return nil
    }

    private func setDistributor(_ kind: Int32, bundleId: String?) {
        lock.lock()
        distributorKind = kind
        // Allocate the marketplace id once and never free or replace it, so the pointer stays valid for the
        // whole app lifetime; the managed side can safely read the string it points to after this lock is released.
        if let bundleId = bundleId, marketplaceBundleId == nil {
            if let copied = strdup(bundleId) {
                marketplaceBundleId = copied
            } else {
                // strdup failed (out of memory): we can't name the marketplace, so don't claim one.
                distributorKind = kDistributorUnavailable
            }
        }
        lock.unlock()
    }

    private func setEnvironment(_ kind: Int32) {
        lock.lock()
        environmentKind = kind
        lock.unlock()
    }

    private func markResolved() {
        lock.lock()
        resolved = true
        lock.unlock()
    }

    @available(iOS 13.0, *)
    private func resolve() async {
        await resolveDistributor()
        await resolveEnvironment()
        markResolved()
    }

    @available(iOS 13.0, *)
    private func resolveDistributor() async {
        #if canImport(MarketplaceKit)
        if #available(iOS 17.4, macCatalyst 17.4, *) {
            do {
                let distributor = try await AppDistributor.current
                switch distributor {
                case .appStore:
                    setDistributor(kDistributorAppStore, bundleId: nil)
                case .testFlight:
                    setDistributor(kDistributorTestFlight, bundleId: nil)
                case .marketplace(let bundleId):
                    setDistributor(kDistributorMarketplace, bundleId: bundleId)
                case .web:
                    setDistributor(kDistributorWeb, bundleId: nil)
                case .other:
                    setDistributor(kDistributorOther, bundleId: nil)
                @unknown default:
                    setDistributor(kDistributorUnavailable, bundleId: nil)
                }
                return
            } catch {
                setDistributor(kDistributorUnavailable, bundleId: nil)
                return
            }
        }
        #endif
        setDistributor(kDistributorUnavailable, bundleId: nil)
    }

    @available(iOS 13.0, *)
    private func resolveEnvironment() async {
        if #available(iOS 16.0, *) {
            do {
                let verification = try await AppTransaction.shared
                let appTransaction: AppTransaction
                switch verification {
                case .verified(let value):
                    appTransaction = value
                case .unverified:
                    // The App Store couldn't verify the transaction signature (possibly tampered). Don't trust it,
                    // and don't fall back to the weaker receipt-name heuristic either: report a distinct error
                    // sentinel that resolves to AccessError.
                    setEnvironment(kEnvironmentError)
                    return
                }

                let environment = appTransaction.environment
                if environment == .production {
                    setEnvironment(kEnvironmentProduction)
                } else if environment == .sandbox {
                    setEnvironment(kEnvironmentSandbox)
                } else if environment == .xcode {
                    setEnvironment(kEnvironmentXcode)
                } else {
                    setEnvironment(kEnvironmentUnavailable)
                }
            } catch {
                setEnvironment(kEnvironmentUnavailable)
            }
        } else {
            setEnvironment(kEnvironmentUnavailable)
        }
    }
}

// Reads the legacy App Store receipt-name heuristic synchronously (available below iOS 16).
// 0 = "receipt" (production), 1 = "sandboxReceipt", 2 = none / unknown.
private func ACTkReadReceiptKind() -> Int32 {
    // The URL can point at the expected receipt path even when no receipt file is present (ad-hoc,
    // enterprise, Xcode installs), so require the file to exist before trusting its name.
    guard let url = Bundle.main.appStoreReceiptURL,
          FileManager.default.fileExists(atPath: url.path) else { return 2 }
    switch url.lastPathComponent {
    case "receipt": return 0
    case "sandboxReceipt": return 1
    default: return 2
    }
}

@_cdecl("ACTk_AppleSource_Start")
public func ACTk_AppleSource_Start() {
    ACTkAppleSourceCache.shared.start()
}

@_cdecl("ACTk_AppleSource_IsResolved")
public func ACTk_AppleSource_IsResolved() -> Int32 {
    ACTkAppleSourceCache.shared.start()
    return ACTkAppleSourceCache.shared.isResolved() ? 1 : 0
}

@_cdecl("ACTk_AppleSource_GetDistributorKind")
public func ACTk_AppleSource_GetDistributorKind() -> Int32 {
    ACTkAppleSourceCache.shared.start()
    return ACTkAppleSourceCache.shared.getDistributorKind()
}

@_cdecl("ACTk_AppleSource_GetMarketplaceBundleId")
public func ACTk_AppleSource_GetMarketplaceBundleId() -> UnsafePointer<CChar>? {
    return ACTkAppleSourceCache.shared.getMarketplaceBundleId()
}

@_cdecl("ACTk_AppleSource_GetEnvironment")
public func ACTk_AppleSource_GetEnvironment() -> Int32 {
    ACTkAppleSourceCache.shared.start()
    return ACTkAppleSourceCache.shared.getEnvironmentKind()
}

@_cdecl("ACTk_AppleSource_GetReceiptKind")
public func ACTk_AppleSource_GetReceiptKind() -> Int32 {
    return ACTkReadReceiptKind()
}
