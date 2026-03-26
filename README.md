# FirestoreSharp

This project is an open source .NET emulator for Google Cloud Firestore. It is designed to provide a local development environment 
for testing and development purposes without the need to connect to the actual Firestore service.

The motivation behind this project is to avoid the memory challenges faced when using the official Firestore emulator, which stores
data in memory. By using a file-based storage approach, FirestoreSharp allows for larger datasets to be handled without running into 
memory limitations.