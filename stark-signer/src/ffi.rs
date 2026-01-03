//! C ABI exports for FFI integration with C#/.NET
//!
//! All functions use C calling convention and return error codes.
//! String outputs are written to caller-provided buffers.

use std::ffi::{c_char, CStr};
use std::ptr;

use starknet::core::crypto::ecdsa_sign;
use starknet::core::types::Felt;
use starknet_crypto::get_public_key;

use crate::get_order_hash;

// Error codes
pub const SUCCESS: i32 = 0;
pub const ERR_NULL_POINTER: i32 = -1;
pub const ERR_INVALID_UTF8: i32 = -2;
pub const ERR_INVALID_HEX: i32 = -3;
pub const ERR_COMPUTATION_FAILED: i32 = -4;
pub const ERR_BUFFER_TOO_SMALL: i32 = -5;
pub const ERR_SIGNING_FAILED: i32 = -6;

/// Helper to convert C string to Rust String
unsafe fn c_str_to_string(ptr: *const c_char) -> Result<String, i32> {
    if ptr.is_null() {
        return Err(ERR_NULL_POINTER);
    }
    CStr::from_ptr(ptr)
        .to_str()
        .map(|s| s.to_string())
        .map_err(|_| ERR_INVALID_UTF8)
}

/// Helper to write string to output buffer
unsafe fn write_to_buffer(s: &str, out: *mut c_char, out_len: usize) -> i32 {
    if out.is_null() {
        return ERR_NULL_POINTER;
    }
    let bytes = s.as_bytes();
    if bytes.len() + 1 > out_len {
        return ERR_BUFFER_TOO_SMALL;
    }
    ptr::copy_nonoverlapping(bytes.as_ptr(), out as *mut u8, bytes.len());
    *out.add(bytes.len()) = 0; // null terminator
    SUCCESS
}

/// Compute Extended DEX order hash (SNIP-12 typed structured data with Poseidon)
///
/// # Parameters
/// - All parameters are null-terminated C strings
/// - `position_id`: decimal string (e.g., "301301")
/// - `base_asset_id_hex`: hex string with 0x prefix (e.g., "0x4254432d36...")
/// - `base_amount`: signed decimal string (positive for BUY, negative for SELL)
/// - `quote_amount`: signed decimal string (negative for BUY, positive for SELL)
/// - `fee_amount`: unsigned decimal string
/// - `expiration`: Unix timestamp in seconds
/// - `salt`: nonce as decimal string
/// - `user_public_key_hex`: Stark public key with 0x prefix
/// - `domain_*`: SNIP-12 domain parameters
/// - `out_hash`: output buffer for hex hash (at least 67 bytes)
///
/// # Returns
/// - 0 on success, negative error code on failure
#[no_mangle]
pub unsafe extern "C" fn stark_get_order_hash(
    position_id: *const c_char,
    base_asset_id_hex: *const c_char,
    base_amount: *const c_char,
    quote_asset_id_hex: *const c_char,
    quote_amount: *const c_char,
    fee_asset_id_hex: *const c_char,
    fee_amount: *const c_char,
    expiration: *const c_char,
    salt: *const c_char,
    user_public_key_hex: *const c_char,
    domain_name: *const c_char,
    domain_version: *const c_char,
    domain_chain_id: *const c_char,
    domain_revision: *const c_char,
    out_hash: *mut c_char,
    out_len: usize,
) -> i32 {
    // Convert all inputs
    let position_id = match c_str_to_string(position_id) {
        Ok(s) => s,
        Err(e) => return e,
    };
    let base_asset_id_hex = match c_str_to_string(base_asset_id_hex) {
        Ok(s) => s,
        Err(e) => return e,
    };
    let base_amount = match c_str_to_string(base_amount) {
        Ok(s) => s,
        Err(e) => return e,
    };
    let quote_asset_id_hex = match c_str_to_string(quote_asset_id_hex) {
        Ok(s) => s,
        Err(e) => return e,
    };
    let quote_amount = match c_str_to_string(quote_amount) {
        Ok(s) => s,
        Err(e) => return e,
    };
    let fee_asset_id_hex = match c_str_to_string(fee_asset_id_hex) {
        Ok(s) => s,
        Err(e) => return e,
    };
    let fee_amount = match c_str_to_string(fee_amount) {
        Ok(s) => s,
        Err(e) => return e,
    };
    let expiration = match c_str_to_string(expiration) {
        Ok(s) => s,
        Err(e) => return e,
    };
    let salt = match c_str_to_string(salt) {
        Ok(s) => s,
        Err(e) => return e,
    };
    let user_public_key_hex = match c_str_to_string(user_public_key_hex) {
        Ok(s) => s,
        Err(e) => return e,
    };
    let domain_name = match c_str_to_string(domain_name) {
        Ok(s) => s,
        Err(e) => return e,
    };
    let domain_version = match c_str_to_string(domain_version) {
        Ok(s) => s,
        Err(e) => return e,
    };
    let domain_chain_id = match c_str_to_string(domain_chain_id) {
        Ok(s) => s,
        Err(e) => return e,
    };
    let domain_revision = match c_str_to_string(domain_revision) {
        Ok(s) => s,
        Err(e) => return e,
    };

    // Call the actual hash function
    let result = get_order_hash(
        position_id,
        base_asset_id_hex,
        base_amount,
        quote_asset_id_hex,
        quote_amount,
        fee_asset_id_hex,
        fee_amount,
        expiration,
        salt,
        user_public_key_hex,
        domain_name,
        domain_version,
        domain_chain_id,
        domain_revision,
    );

    match result {
        Ok(hash) => {
            let hash_hex = format!("0x{:064x}", hash);
            write_to_buffer(&hash_hex, out_hash, out_len)
        }
        Err(_) => ERR_COMPUTATION_FAILED,
    }
}

/// Sign a message hash with a Stark private key
///
/// # Parameters
/// - `private_key_hex`: private key as hex string with 0x prefix
/// - `message_hash_hex`: message hash as hex string with 0x prefix
/// - `out_r`, `out_s`: output buffers for signature components (at least 67 bytes each)
///
/// # Returns
/// - 0 on success, negative error code on failure
#[no_mangle]
pub unsafe extern "C" fn stark_sign(
    private_key_hex: *const c_char,
    message_hash_hex: *const c_char,
    out_r: *mut c_char,
    out_s: *mut c_char,
    out_r_len: usize,
    out_s_len: usize,
) -> i32 {
    let private_key_hex = match c_str_to_string(private_key_hex) {
        Ok(s) => s,
        Err(e) => return e,
    };
    let message_hash_hex = match c_str_to_string(message_hash_hex) {
        Ok(s) => s,
        Err(e) => return e,
    };

    // Parse hex strings to Felt
    let private_key = match Felt::from_hex(&private_key_hex) {
        Ok(f) => f,
        Err(_) => return ERR_INVALID_HEX,
    };
    let message_hash = match Felt::from_hex(&message_hash_hex) {
        Ok(f) => f,
        Err(_) => return ERR_INVALID_HEX,
    };

    // Sign the message
    let signature = match ecdsa_sign(&private_key, &message_hash) {
        Ok(sig) => sig,
        Err(_) => return ERR_SIGNING_FAILED,
    };

    // Write outputs
    let r_hex = format!("0x{:064x}", signature.r);
    let s_hex = format!("0x{:064x}", signature.s);

    let result = write_to_buffer(&r_hex, out_r, out_r_len);
    if result != SUCCESS {
        return result;
    }
    write_to_buffer(&s_hex, out_s, out_s_len)
}

/// Derive public key from private key
///
/// # Parameters
/// - `private_key_hex`: private key as hex string with 0x prefix
/// - `out_public_key`: output buffer for public key (at least 67 bytes)
///
/// # Returns
/// - 0 on success, negative error code on failure
#[no_mangle]
pub unsafe extern "C" fn stark_get_public_key(
    private_key_hex: *const c_char,
    out_public_key: *mut c_char,
    out_len: usize,
) -> i32 {
    let private_key_hex = match c_str_to_string(private_key_hex) {
        Ok(s) => s,
        Err(e) => return e,
    };

    let private_key = match Felt::from_hex(&private_key_hex) {
        Ok(f) => f,
        Err(_) => return ERR_INVALID_HEX,
    };

    let public_key = get_public_key(&private_key);
    let public_key_hex = format!("0x{:064x}", public_key);

    write_to_buffer(&public_key_hex, out_public_key, out_len)
}

/// Get error message for an error code
#[no_mangle]
pub extern "C" fn stark_get_error_message(error_code: i32) -> *const c_char {
    static SUCCESS_MSG: &[u8] = b"Success\0";
    static NULL_POINTER_MSG: &[u8] = b"Null pointer provided\0";
    static INVALID_UTF8_MSG: &[u8] = b"Invalid UTF-8 string\0";
    static INVALID_HEX_MSG: &[u8] = b"Invalid hexadecimal value\0";
    static COMPUTATION_FAILED_MSG: &[u8] = b"Hash computation failed\0";
    static BUFFER_TOO_SMALL_MSG: &[u8] = b"Output buffer too small\0";
    static SIGNING_FAILED_MSG: &[u8] = b"Signing operation failed\0";
    static UNKNOWN_MSG: &[u8] = b"Unknown error\0";

    let msg = match error_code {
        SUCCESS => SUCCESS_MSG,
        ERR_NULL_POINTER => NULL_POINTER_MSG,
        ERR_INVALID_UTF8 => INVALID_UTF8_MSG,
        ERR_INVALID_HEX => INVALID_HEX_MSG,
        ERR_COMPUTATION_FAILED => COMPUTATION_FAILED_MSG,
        ERR_BUFFER_TOO_SMALL => BUFFER_TOO_SMALL_MSG,
        ERR_SIGNING_FAILED => SIGNING_FAILED_MSG,
        _ => UNKNOWN_MSG,
    };

    msg.as_ptr() as *const c_char
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::ffi::CString;

    #[test]
    fn test_stark_get_order_hash_ffi() {
        let position_id = CString::new("100").unwrap();
        let base_asset_id = CString::new("0x2").unwrap();
        let base_amount = CString::new("100").unwrap();
        let quote_asset_id = CString::new("0x1").unwrap();
        let quote_amount = CString::new("-156").unwrap();
        let fee_asset_id = CString::new("0x1").unwrap();
        let fee_amount = CString::new("74").unwrap();
        let expiration = CString::new("100").unwrap();
        let salt = CString::new("123").unwrap();
        let user_public_key = CString::new("0x5d05989e9302dcebc74e241001e3e3ac3f4402ccf2f8e6f74b034b07ad6a904").unwrap();
        let domain_name = CString::new("Perpetuals").unwrap();
        let domain_version = CString::new("v0").unwrap();
        let domain_chain_id = CString::new("SN_SEPOLIA").unwrap();
        let domain_revision = CString::new("1").unwrap();

        let mut out_hash = vec![0u8; 128];

        let result = unsafe {
            stark_get_order_hash(
                position_id.as_ptr(),
                base_asset_id.as_ptr(),
                base_amount.as_ptr(),
                quote_asset_id.as_ptr(),
                quote_amount.as_ptr(),
                fee_asset_id.as_ptr(),
                fee_amount.as_ptr(),
                expiration.as_ptr(),
                salt.as_ptr(),
                user_public_key.as_ptr(),
                domain_name.as_ptr(),
                domain_version.as_ptr(),
                domain_chain_id.as_ptr(),
                domain_revision.as_ptr(),
                out_hash.as_mut_ptr() as *mut c_char,
                out_hash.len(),
            )
        };

        assert_eq!(result, SUCCESS);

        let hash_str = unsafe { CStr::from_ptr(out_hash.as_ptr() as *const c_char) }
            .to_str()
            .unwrap();

        // Expected hash from lib.rs test
        assert_eq!(
            hash_str,
            "0x04de4c009e0d0c5a70a7da0e2039fb2b99f376d53496f89d9f437e736add6b48"
        );
    }

    #[test]
    fn test_stark_sign_ffi() {
        // Use a test private key from Python SDK fixtures
        let private_key = CString::new("0x7a7ff6fd3cab02ccdcd4a572563f5976f8976899b03a39773795a3c486d4986").unwrap();
        let message_hash = CString::new("0x04de4c009e0d0c5a70a7da0e2039fb2b99f376d53496f89d9f437e736add6b48").unwrap();

        let mut out_r = vec![0u8; 128];
        let mut out_s = vec![0u8; 128];

        let result = unsafe {
            stark_sign(
                private_key.as_ptr(),
                message_hash.as_ptr(),
                out_r.as_mut_ptr() as *mut c_char,
                out_s.as_mut_ptr() as *mut c_char,
                out_r.len(),
                out_s.len(),
            )
        };

        assert_eq!(result, SUCCESS);

        let r_str = unsafe { CStr::from_ptr(out_r.as_ptr() as *const c_char) }
            .to_str()
            .unwrap();
        let s_str = unsafe { CStr::from_ptr(out_s.as_ptr() as *const c_char) }
            .to_str()
            .unwrap();

        // Verify the signature starts with 0x and has reasonable length
        assert!(r_str.starts_with("0x"));
        assert!(s_str.starts_with("0x"));
        assert!(r_str.len() >= 66); // 0x + 64 hex chars
        assert!(s_str.len() >= 66);
    }

    #[test]
    fn test_stark_get_public_key_ffi() {
        // Use a test private key from Python SDK fixtures
        let private_key = CString::new("0x7a7ff6fd3cab02ccdcd4a572563f5976f8976899b03a39773795a3c486d4986").unwrap();
        let mut out_public_key = vec![0u8; 128];

        let result = unsafe {
            stark_get_public_key(
                private_key.as_ptr(),
                out_public_key.as_mut_ptr() as *mut c_char,
                out_public_key.len(),
            )
        };

        assert_eq!(result, SUCCESS);

        let pub_key_str = unsafe { CStr::from_ptr(out_public_key.as_ptr() as *const c_char) }
            .to_str()
            .unwrap();

        assert!(pub_key_str.starts_with("0x"));
        assert!(pub_key_str.len() >= 66);
    }
}
