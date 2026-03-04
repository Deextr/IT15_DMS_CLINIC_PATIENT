/**
 * Google Drive Picker Integration for DMS_CPMS
 * Handles OAuth 2.0 authentication and Google Picker UI for file selection.
 * Only SuperAdmin/Admin roles can use this feature.
 */

let googleDriveConfig = null;
let googleAccessToken = null;
let googleTokenClient = null;
let pickerApiLoaded = false;
let gisLoaded = false;

/**
 * Loads Google API config from the server.
 */
async function loadGoogleDriveConfig() {
    if (googleDriveConfig) return googleDriveConfig;
    try {
        const response = await fetch('/GoogleDrive/GetConfig');
        if (response.ok) {
            googleDriveConfig = await response.json();
            return googleDriveConfig;
        }
    } catch (error) {
        console.error('Failed to load Google Drive config:', error);
    }
    return null;
}

/**
 * Dynamically loads Google API (Picker) and Google Identity Services scripts.
 */
function loadGoogleScripts() {
    return new Promise((resolve, reject) => {
        let scriptsToLoad = 0;
        let scriptsLoaded = 0;

        function checkAllLoaded() {
            scriptsLoaded++;
            if (scriptsLoaded >= scriptsToLoad) resolve();
        }

        // Load Google API (for Picker)
        if (!document.getElementById('google-api-script')) {
            scriptsToLoad++;
            const gapiScript = document.createElement('script');
            gapiScript.id = 'google-api-script';
            gapiScript.src = 'https://apis.google.com/js/api.js';
            gapiScript.onload = () => {
                gapi.load('picker', () => {
                    pickerApiLoaded = true;
                    checkAllLoaded();
                });
            };
            gapiScript.onerror = () => reject(new Error('Failed to load Google API script'));
            document.head.appendChild(gapiScript);
        } else if (!pickerApiLoaded) {
            scriptsToLoad++;
            gapi.load('picker', () => {
                pickerApiLoaded = true;
                checkAllLoaded();
            });
        }

        // Load Google Identity Services (for OAuth)
        if (!document.getElementById('google-gis-script')) {
            scriptsToLoad++;
            const gisScript = document.createElement('script');
            gisScript.id = 'google-gis-script';
            gisScript.src = 'https://accounts.google.com/gsi/client';
            gisScript.onload = () => {
                gisLoaded = true;
                checkAllLoaded();
            };
            gisScript.onerror = () => reject(new Error('Failed to load Google Identity Services'));
            document.head.appendChild(gisScript);
        } else {
            gisLoaded = true;
        }

        if (scriptsToLoad === 0) resolve();
    });
}

/**
 * Switches upload modal between device and Google Drive modes.
 */
function selectUploadSource(source) {
    const deviceCard = document.getElementById('sourceDeviceCard');
    const googleCard = document.getElementById('sourceGoogleCard');
    const deviceSection = document.getElementById('deviceFileSection');
    const googleSection = document.getElementById('googleDriveSection');
    const sourceTypeInput = document.getElementById('uploadSourceType');
    const fileInput = document.getElementById('uploadFileInput');

    if (source === 'device') {
        deviceCard?.classList.add('active');
        googleCard?.classList.remove('active');
        document.getElementById('sourceDeviceRadio').checked = true;
        deviceSection.style.display = 'block';
        googleSection.style.display = 'none';
        sourceTypeInput.value = 'device';
        if (fileInput) fileInput.required = true;
        clearGoogleDriveSelection();
    } else if (source === 'googledrive') {
        googleCard?.classList.add('active');
        deviceCard?.classList.remove('active');
        document.getElementById('sourceGoogleRadio').checked = true;
        deviceSection.style.display = 'none';
        googleSection.style.display = 'block';
        sourceTypeInput.value = 'googledrive';
        if (fileInput) {
            fileInput.required = false;
            fileInput.value = '';
        }
    }
}

/**
 * Opens Google Drive Picker. Handles authentication first if needed.
 */
async function openGoogleDrivePicker() {
    const loadingEl = document.getElementById('googleDriveLoading');
    const errorEl = document.getElementById('googleDriveError');
    const pickerBtn = document.getElementById('googleDrivePickerBtn');

    errorEl.style.display = 'none';

    try {
        loadingEl.style.display = 'flex';
        pickerBtn.disabled = true;

        const config = await loadGoogleDriveConfig();
        if (!config || !config.clientId || !config.apiKey) {
            throw new Error('Google Drive integration is not configured. Please contact the administrator.');
        }

        await loadGoogleScripts();

        // If we already have a valid token, open picker directly
        if (googleAccessToken) {
            createAndShowPicker(config);
            return;
        }

        // Initialize token client and trigger sign-in
        googleTokenClient = google.accounts.oauth2.initTokenClient({
            client_id: config.clientId,
            scope: 'https://www.googleapis.com/auth/drive.readonly',
            callback: (tokenResponse) => {
                if (tokenResponse && tokenResponse.access_token) {
                    googleAccessToken = tokenResponse.access_token;
                    loadingEl.style.display = 'none';
                    pickerBtn.disabled = false;
                    createAndShowPicker(config);
                } else {
                    loadingEl.style.display = 'none';
                    pickerBtn.disabled = false;
                    showGoogleDriveError('Authentication failed. Please try again.');
                }
            },
            error_callback: (error) => {
                loadingEl.style.display = 'none';
                pickerBtn.disabled = false;
                console.error('Google auth error:', error);
                showGoogleDriveError('Authentication was cancelled or failed.');
            }
        });

        loadingEl.style.display = 'none';
        pickerBtn.disabled = false;
        googleTokenClient.requestAccessToken({ prompt: '' });

    } catch (error) {
        console.error('Google Drive Picker error:', error);
        loadingEl.style.display = 'none';
        pickerBtn.disabled = false;
        showGoogleDriveError(error.message || 'Failed to open Google Drive. Please try again.');
    }
}

/**
 * Creates and shows the Google Picker dialog.
 */
function createAndShowPicker(config) {
    if (!pickerApiLoaded || !googleAccessToken) {
        showGoogleDriveError('Google Picker is not ready. Please try again.');
        return;
    }

    try {
        const allowedMimeTypes = [
            'application/pdf',
            'image/jpeg',
            'image/png',
            'image/gif',
            'text/csv',
            'text/plain',
            'application/msword',
            'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
            'application/vnd.ms-excel',
            'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
            'application/vnd.google-apps.document',
            'application/vnd.google-apps.spreadsheet'
        ].join(',');

        const docsView = new google.picker.DocsView(google.picker.ViewId.DOCS)
            .setMimeTypes(allowedMimeTypes)
            .setIncludeFolders(false)
            .setSelectFolderEnabled(false)
            .setMode(google.picker.DocsViewMode.LIST);

        const picker = new google.picker.PickerBuilder()
            .setAppId(config.appId)
            .setOAuthToken(googleAccessToken)
            .setDeveloperKey(config.apiKey)
            .addView(docsView)
            .addView(new google.picker.DocsUploadView())
            .setSelectableMimeTypes(allowedMimeTypes)
            .setCallback(pickerCallback)
            .setTitle('Select a Document from Google Drive')
            .setMaxItems(1)
            .build();

        picker.setVisible(true);

        // Add class to body so CSS can lower modal z-index while picker is open
        document.body.classList.add('google-picker-active');
    } catch (error) {
        console.error('Error creating picker:', error);
        document.body.classList.remove('google-picker-active');
        showGoogleDriveError('Failed to open file picker. Please try again.');
    }
}

/**
 * Picker callback — handles file selection.
 */
function pickerCallback(data) {
    // Remove the z-index override when picker closes (PICKED or CANCEL)
    if (data.action === google.picker.Action.PICKED || data.action === google.picker.Action.CANCEL) {
        document.body.classList.remove('google-picker-active');
    }

    if (data.action === google.picker.Action.PICKED) {
        const file = data.docs[0];
        if (file) {
            document.getElementById('googleDriveFileId').value = file.id;
            document.getElementById('googleDriveAccessToken').value = googleAccessToken;

            const selectedFileEl = document.getElementById('googleDriveSelectedFile');
            const fileNameEl = document.getElementById('googleDriveFileName');
            const pickerBtn = document.getElementById('googleDrivePickerBtn');
            const errorEl = document.getElementById('googleDriveError');

            fileNameEl.textContent = file.name;
            selectedFileEl.style.display = 'flex';
            pickerBtn.style.display = 'none';
            errorEl.style.display = 'none';
        }
    }
}

/**
 * Clears Google Drive file selection.
 */
function clearGoogleDriveSelection() {
    const fileIdEl = document.getElementById('googleDriveFileId');
    const tokenEl = document.getElementById('googleDriveAccessToken');
    const selectedEl = document.getElementById('googleDriveSelectedFile');
    const pickerBtn = document.getElementById('googleDrivePickerBtn');
    const errorEl = document.getElementById('googleDriveError');

    if (fileIdEl) fileIdEl.value = '';
    if (tokenEl) tokenEl.value = '';
    if (selectedEl) selectedEl.style.display = 'none';
    if (pickerBtn) pickerBtn.style.display = '';
    if (errorEl) errorEl.style.display = 'none';
}

/**
 * Shows error in Google Drive section.
 */
function showGoogleDriveError(message) {
    const errorEl = document.getElementById('googleDriveError');
    const errorMsg = document.getElementById('googleDriveErrorMsg');
    if (errorEl && errorMsg) {
        errorMsg.textContent = message;
        errorEl.style.display = 'flex';
    }
}

/**
 * Handles upload form submission — supports both device and Google Drive.
 * Replaces the existing upload submit handler.
 */
async function handleUploadFormSubmit(e) {
    e.preventDefault();

    const form = e.target;
    const submitBtn = document.getElementById('uploadSubmitBtn');
    const normalText = submitBtn.querySelector('.normal-text');
    const loadingText = submitBtn.querySelector('.loading-text');
    const sourceType = document.getElementById('uploadSourceType').value;

    submitBtn.disabled = true;
    normalText.style.display = 'none';
    loadingText.style.display = 'inline';

    try {
        const token = document.querySelector('#uploadDocumentForm input[name="__RequestVerificationToken"]').value;

        if (sourceType === 'googledrive') {
            // ── Google Drive upload ──
            const fileId = document.getElementById('googleDriveFileId').value;
            const accessToken = document.getElementById('googleDriveAccessToken').value;
            const documentTitle = document.getElementById('uploadDocumentTitle').value;
            const documentType = document.getElementById('uploadDocumentType').value;
            const otherDocType = document.getElementById('uploadOtherDocumentType')?.value || '';
            const patientId = document.getElementById('uploadPatientId').value;

            if (!fileId || !accessToken) {
                showNotification('Please select a file from Google Drive.', 'error');
                return;
            }
            if (!documentTitle) {
                showNotification('Please enter a document title.', 'error');
                return;
            }
            if (!documentType) {
                showNotification('Please select a document type.', 'error');
                return;
            }

            const response = await fetch('/GoogleDrive/UploadFromDrive', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': token
                },
                body: JSON.stringify({
                    patientID: parseInt(patientId),
                    documentTitle: documentTitle,
                    documentType: documentType,
                    otherDocumentType: otherDocType,
                    fileId: fileId,
                    accessToken: accessToken
                })
            });

            if (response.ok) {
                const result = await response.json();
                if (result.success) {
                    showNotification(result.message || 'Document uploaded from Google Drive successfully!', 'success');
                    bootstrap.Modal.getInstance(document.getElementById('nestedUploadModal')).hide();
                    loadPatientDocuments(currentPatientId, 1);
                    document.getElementById('googleDriveAccessToken').value = '';
                } else {
                    showNotification(result.message || 'Failed to upload from Google Drive.', 'error');
                }
            } else {
                showNotification('Failed to upload from Google Drive. Please try again.', 'error');
            }
        } else {
            // ── Standard device upload (existing logic) ──
            const fileInput = document.getElementById('uploadFileInput');
            if (!fileInput || !fileInput.files.length) {
                showNotification('Please select a file to upload.', 'error');
                return;
            }

            const formData = new FormData(form);
            const response = await fetch('/PatientDocuments/UploadAjax', {
                method: 'POST',
                body: formData,
                headers: {
                    'RequestVerificationToken': token
                }
            });

            if (response.ok) {
                const result = await response.json();
                if (result.success) {
                    showNotification('Document uploaded successfully!', 'success');
                    bootstrap.Modal.getInstance(document.getElementById('nestedUploadModal')).hide();
                    loadPatientDocuments(currentPatientId, 1);
                } else {
                    showNotification(result.message || 'Failed to upload document.', 'error');
                }
            } else {
                showNotification('Failed to upload document. Please try again.', 'error');
            }
        }
    } catch (error) {
        console.error('Upload error:', error);
        showNotification('An error occurred during upload.', 'error');
    } finally {
        submitBtn.disabled = false;
        normalText.style.display = 'inline';
        loadingText.style.display = 'none';
    }
}

/**
 * Resets upload modal to default (device) state.
 */
function resetUploadModal() {
    selectUploadSource('device');
    clearGoogleDriveSelection();

    const form = document.getElementById('uploadDocumentForm');
    if (form) form.reset();

    const otherContainer = document.getElementById('uploadOtherTypeContainer');
    if (otherContainer) otherContainer.style.display = 'none';

    const deviceRadio = document.getElementById('sourceDeviceRadio');
    if (deviceRadio) deviceRadio.checked = true;
}
