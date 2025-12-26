//! Native Stark signature library for GridBot.Extended
//!
//! Provides C ABI exports for Stark curve cryptographic operations:
//! - ECDSA signing with Stark private keys
//! - Public key derivation
//! - Pedersen hashing for message construction
//!
//! # Safety
//! All exported functions use raw pointers for C interop.
//! Callers must ensure valid UTF-8 strings and sufficient buffer sizes.

use std::ffi::{CStr, CString};
use std::os::raw::c_char;

use starknet_crypto::{pedersen_hash, sign, get_public_key, Felt};

/// Result codes for native operations
const SUCCESS: i32 = 0;
const ERR_NULL_POINTER: i32 = -1;
const ERR_INVALID_UTF8: i32 = -2;
const ERR_INVALID_HEX: i32 = -3;
const ERR_SIGNING_FAILED: i32 = -4;
const ERR_BUFFER_TOO_SMALL: i32 = -5;

/// Signs a message hash with a Stark private key.
///
/// # Arguments
/// * `private_key` - Hex-encoded private key (with or without 0x prefix)
/// * `message_hash` - Hex-encoded message hash to sign
/// * `out_r` - Output buffer for signature r component (must be at least 67 bytes)
/// * `out_s` - Output buffer for signature s component (must be at least 67 bytes)
/// * `out_r_len` - Size of out_r buffer
/// * `out_s_len` - Size of out_s buffer
///
/// # Returns
/// * 0 on success
/// * Negative error code on failure
#[no_mangle]
pub unsafe extern "C" fn stark_sign(
    private_key: *const c_char,
    message_hash: *const c_char,
    out_r: *mut c_char,
    out_s: *mut c_char,
    out_r_len: usize,
    out_s_len: usize,
) -> i32 {
    // Validate inputs
    if private_key.is_null() || message_hash.is_null() || out_r.is_null() || out_s.is_null() {
        return ERR_NULL_POINTER;
    }

    // Parse private key
    let pk_str = match CStr::from_ptr(private_key).to_str() {
        Ok(s) => s.trim_start_matches("0x"),
        Err(_) => return ERR_INVALID_UTF8,
    };

    let private_key_felt = match Felt::from_hex(pk_str) {
        Ok(f) => f,
        Err(_) => return ERR_INVALID_HEX,
    };

    // Parse message hash
    let hash_str = match CStr::from_ptr(message_hash).to_str() {
        Ok(s) => s.trim_start_matches("0x"),
        Err(_) => return ERR_INVALID_UTF8,
    };

    let message_hash_felt = match Felt::from_hex(hash_str) {
        Ok(f) => f,
        Err(_) => return ERR_INVALID_HEX,
    };

    // Sign the message
    let signature = match sign(&private_key_felt, &message_hash_felt, &Felt::from(1u64)) {
        Ok(sig) => sig,
        Err(_) => return ERR_SIGNING_FAILED,
    };

    // Format outputs as hex with 0x prefix
    let r_hex = format!("0x{:064x}", signature.r);
    let s_hex = format!("0x{:064x}", signature.s);

    // Check buffer sizes
    if out_r_len < r_hex.len() + 1 || out_s_len < s_hex.len() + 1 {
        return ERR_BUFFER_TOO_SMALL;
    }

    // Write outputs
    let r_cstr = match CString::new(r_hex) {
        Ok(s) => s,
        Err(_) => return ERR_SIGNING_FAILED,
    };
    let s_cstr = match CString::new(s_hex) {
        Ok(s) => s,
        Err(_) => return ERR_SIGNING_FAILED,
    };

    std::ptr::copy_nonoverlapping(r_cstr.as_ptr(), out_r, r_cstr.as_bytes_with_nul().len());
    std::ptr::copy_nonoverlapping(s_cstr.as_ptr(), out_s, s_cstr.as_bytes_with_nul().len());

    SUCCESS
}

/// Derives the public key from a Stark private key.
///
/// # Arguments
/// * `private_key` - Hex-encoded private key (with or without 0x prefix)
/// * `out_public_key` - Output buffer for public key (must be at least 67 bytes)
/// * `out_len` - Size of output buffer
///
/// # Returns
/// * 0 on success
/// * Negative error code on failure
#[no_mangle]
pub unsafe extern "C" fn stark_get_public_key(
    private_key: *const c_char,
    out_public_key: *mut c_char,
    out_len: usize,
) -> i32 {
    if private_key.is_null() || out_public_key.is_null() {
        return ERR_NULL_POINTER;
    }

    let pk_str = match CStr::from_ptr(private_key).to_str() {
        Ok(s) => s.trim_start_matches("0x"),
        Err(_) => return ERR_INVALID_UTF8,
    };

    let private_key_felt = match Felt::from_hex(pk_str) {
        Ok(f) => f,
        Err(_) => return ERR_INVALID_HEX,
    };

    let public_key = get_public_key(&private_key_felt);
    let pub_hex = format!("0x{:064x}", public_key);

    if out_len < pub_hex.len() + 1 {
        return ERR_BUFFER_TOO_SMALL;
    }

    let pub_cstr = match CString::new(pub_hex) {
        Ok(s) => s,
        Err(_) => return ERR_SIGNING_FAILED,
    };

    std::ptr::copy_nonoverlapping(pub_cstr.as_ptr(), out_public_key, pub_cstr.as_bytes_with_nul().len());

    SUCCESS
}

/// Computes the Pedersen hash of two field elements.
///
/// # Arguments
/// * `a` - First hex-encoded field element
/// * `b` - Second hex-encoded field element
/// * `out_hash` - Output buffer for hash result (must be at least 67 bytes)
/// * `out_len` - Size of output buffer
///
/// # Returns
/// * 0 on success
/// * Negative error code on failure
#[no_mangle]
pub unsafe extern "C" fn stark_pedersen_hash(
    a: *const c_char,
    b: *const c_char,
    out_hash: *mut c_char,
    out_len: usize,
) -> i32 {
    if a.is_null() || b.is_null() || out_hash.is_null() {
        return ERR_NULL_POINTER;
    }

    let a_str = match CStr::from_ptr(a).to_str() {
        Ok(s) => s.trim_start_matches("0x"),
        Err(_) => return ERR_INVALID_UTF8,
    };

    let b_str = match CStr::from_ptr(b).to_str() {
        Ok(s) => s.trim_start_matches("0x"),
        Err(_) => return ERR_INVALID_UTF8,
    };

    let a_felt = match Felt::from_hex(a_str) {
        Ok(f) => f,
        Err(_) => return ERR_INVALID_HEX,
    };

    let b_felt = match Felt::from_hex(b_str) {
        Ok(f) => f,
        Err(_) => return ERR_INVALID_HEX,
    };

    let hash = pedersen_hash(&a_felt, &b_felt);
    let hash_hex = format!("0x{:064x}", hash);

    if out_len < hash_hex.len() + 1 {
        return ERR_BUFFER_TOO_SMALL;
    }

    let hash_cstr = match CString::new(hash_hex) {
        Ok(s) => s,
        Err(_) => return ERR_SIGNING_FAILED,
    };

    std::ptr::copy_nonoverlapping(hash_cstr.as_ptr(), out_hash, hash_cstr.as_bytes_with_nul().len());

    SUCCESS
}

/// Computes the Pedersen hash of multiple field elements.
/// Uses the standard chained Pedersen hash: h(h(h(0, a), b), c)...
///
/// # Arguments
/// * `elements` - Array of hex-encoded field elements (null-terminated strings)
/// * `count` - Number of elements in the array
/// * `out_hash` - Output buffer for hash result (must be at least 67 bytes)
/// * `out_len` - Size of output buffer
///
/// # Returns
/// * 0 on success
/// * Negative error code on failure
#[no_mangle]
pub unsafe extern "C" fn stark_pedersen_hash_many(
    elements: *const *const c_char,
    count: usize,
    out_hash: *mut c_char,
    out_len: usize,
) -> i32 {
    if elements.is_null() || out_hash.is_null() {
        return ERR_NULL_POINTER;
    }

    let mut result = Felt::ZERO;

    for i in 0..count {
        let elem_ptr = *elements.add(i);
        if elem_ptr.is_null() {
            return ERR_NULL_POINTER;
        }

        let elem_str = match CStr::from_ptr(elem_ptr).to_str() {
            Ok(s) => s.trim_start_matches("0x"),
            Err(_) => return ERR_INVALID_UTF8,
        };

        let elem_felt = match Felt::from_hex(elem_str) {
            Ok(f) => f,
            Err(_) => return ERR_INVALID_HEX,
        };

        result = pedersen_hash(&result, &elem_felt);
    }

    let hash_hex = format!("0x{:064x}", result);

    if out_len < hash_hex.len() + 1 {
        return ERR_BUFFER_TOO_SMALL;
    }

    let hash_cstr = match CString::new(hash_hex) {
        Ok(s) => s,
        Err(_) => return ERR_SIGNING_FAILED,
    };

    std::ptr::copy_nonoverlapping(hash_cstr.as_ptr(), out_hash, hash_cstr.as_bytes_with_nul().len());

    SUCCESS
}

/// Returns the error message for a given error code.
///
/// # Arguments
/// * `error_code` - The error code to get a message for
///
/// # Returns
/// * Static string pointer describing the error
#[no_mangle]
pub extern "C" fn stark_get_error_message(error_code: i32) -> *const c_char {
    let msg = match error_code {
        SUCCESS => "Success\0",
        ERR_NULL_POINTER => "Null pointer provided\0",
        ERR_INVALID_UTF8 => "Invalid UTF-8 string\0",
        ERR_INVALID_HEX => "Invalid hexadecimal value\0",
        ERR_SIGNING_FAILED => "Signing operation failed\0",
        ERR_BUFFER_TOO_SMALL => "Output buffer too small\0",
        _ => "Unknown error\0",
    };
    msg.as_ptr() as *const c_char
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::ffi::CString;

    #[test]
    fn test_get_public_key() {
        // Known test vector
        let private_key = CString::new("0x1").unwrap();
        let mut out_buf = vec![0u8; 128];

        unsafe {
            let result = stark_get_public_key(
                private_key.as_ptr(),
                out_buf.as_mut_ptr() as *mut c_char,
                out_buf.len(),
            );
            assert_eq!(result, SUCCESS);

            let out_str = CStr::from_ptr(out_buf.as_ptr() as *const c_char)
                .to_str()
                .unwrap();
            assert!(out_str.starts_with("0x"));
            assert_eq!(out_str.len(), 66); // 0x + 64 hex chars
        }
    }

    #[test]
    fn test_sign_and_format() {
        let private_key = CString::new("0x1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef").unwrap();
        let message = CString::new("0xdeadbeef").unwrap();
        let mut r_buf = vec![0u8; 128];
        let mut s_buf = vec![0u8; 128];

        unsafe {
            let result = stark_sign(
                private_key.as_ptr(),
                message.as_ptr(),
                r_buf.as_mut_ptr() as *mut c_char,
                s_buf.as_mut_ptr() as *mut c_char,
                r_buf.len(),
                s_buf.len(),
            );
            assert_eq!(result, SUCCESS);

            let r_str = CStr::from_ptr(r_buf.as_ptr() as *const c_char)
                .to_str()
                .unwrap();
            let s_str = CStr::from_ptr(s_buf.as_ptr() as *const c_char)
                .to_str()
                .unwrap();

            assert!(r_str.starts_with("0x"));
            assert!(s_str.starts_with("0x"));
        }
    }

    #[test]
    fn test_pedersen_hash() {
        let a = CString::new("0x1").unwrap();
        let b = CString::new("0x2").unwrap();
        let mut out_buf = vec![0u8; 128];

        unsafe {
            let result = stark_pedersen_hash(
                a.as_ptr(),
                b.as_ptr(),
                out_buf.as_mut_ptr() as *mut c_char,
                out_buf.len(),
            );
            assert_eq!(result, SUCCESS);

            let hash_str = CStr::from_ptr(out_buf.as_ptr() as *const c_char)
                .to_str()
                .unwrap();
            assert!(hash_str.starts_with("0x"));
        }
    }
}
