﻿using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security;

namespace Scorpion_MDB
{
    public class ScorpionMicroDB
    {
        internal string S_NULL = "", S_No = "false";
        public const int K_DEFAULT_SLOT_SIZE = 50000;
        private const int K_DEFAULT_STRT_SIZE = 0;
        private static readonly string[] Field_Type_Data = { "dat", "num", "bin" };
        private static readonly List<string> Query_Types = new List<string>(4) { "data", "tag", "meta", "type" };
        private ArrayList mem_db_ref = ArrayList.Synchronized(new ArrayList());
        private ArrayList mem_db_path = ArrayList.Synchronized(new ArrayList());
        private ArrayList mem_db = ArrayList.Synchronized(new ArrayList());
        private Scorpion.Crypto.Cryptographer cryptographer = new Scorpion.Crypto.Cryptographer();

        public bool checkLoaded(string dbname)
        {
            if (mem_db_ref.IndexOf(dbname) > -1)
                return true;
            return false;
        }

        public void createDB(string path, bool spill, string password)
        {
            //A scorpion database is composed of three data fields:
            /*
            * Tag: A tag consists of a super identifier. This can be a group to which the data belongs to such as 'Row1'
            * SubTag: A subtag consists of an identifier of what the data represents within the tag, for example 'Name' or 'Age' within the Tag 'Row1'
            * Data: Data contained in the database
            */

            ArrayList s_subtag = new ArrayList(K_DEFAULT_SLOT_SIZE);
            ArrayList s_tag = new ArrayList(K_DEFAULT_SLOT_SIZE);
            ArrayList s_data = new ArrayList(K_DEFAULT_SLOT_SIZE);

            //Group tag allows us to group multiple databases in clusters incase one database gets to it's maximum size data can spillover into a new database with the same groupname
            FileInfo fnf_db = new FileInfo(path);

            //Create SHA seed
            SecureString s_seed = cryptographer.Create_Seed();
            //Create SHA out of seed
            string sha_ = cryptographer.SHA_SS(s_seed);
            /*DATA  TAG */
            ArrayList db = new ArrayList (3) { s_data, s_tag, s_subtag };

            File.WriteAllBytes(path, ScorpionAES.ScorpionAES.encryptData(cryptographer.Array_To_String(db), password));

            path = null;
            return;
        }

        public void closeDB(string name)
        {
            //Close and remove
            lock(mem_db) lock(mem_db_path) lock(mem_db_ref)
            {
                ((ArrayList)mem_db[mem_db_ref.IndexOf(name)]).Clear();
                ((ArrayList)mem_db[mem_db_ref.IndexOf(name)]).TrimToSize();
                mem_db.RemoveAt(mem_db_ref.IndexOf(name));
                mem_db.TrimToSize();
                mem_db_path.RemoveAt(mem_db_ref.IndexOf(name));
                mem_db_path.TrimToSize();
                mem_db_ref.Remove(name);
                mem_db_ref.TrimToSize();
            }
            return;
        }

        public void loadDB(string path, string name, string password)
        {
            //File.Decrypt(path);
            byte[] b = File.ReadAllBytes(path);
            string xml = ScorpionAES.ScorpionAES.decryptData(b, password);
            object db_object = cryptographer.String_To_Array(xml);

            lock(mem_db) lock(mem_db_path) lock(mem_db_ref)
            {
                if (!mem_db_ref.Contains(name) && !mem_db_path.Contains(path))
                {
                    mem_db.Add(db_object);
                    mem_db_ref.Add(name);
                    mem_db_path.Add(path);
                    Console.WriteLine("Opened Database: [{0}] as [{0}]", path, name);
                }
                else
                    Console.Write("Database [{0}]/[{1}] already in memory", path, name);
            }
            return;
        }

        public void reloadDB(string name, string password)
        {
            int ndx = mem_db_ref.IndexOf(name);
            string path = (string)mem_db_path[ndx];

            closeDB(name);
            loadDB(path, name, password);

            Console.WriteLine("Reloaded Database: [{0}] as [{1}]", path, name);
            return;
        }

        public void saveDB(string name, string password)
        {
            lock(mem_db) lock(mem_db_path) lock(mem_db_ref)
            {
                int ndx = mem_db_ref.IndexOf(name);
                File.WriteAllBytes((string)mem_db_path[ndx], ScorpionAES.ScorpionAES.encryptData(cryptographer.Array_To_String((ArrayList)mem_db[ndx]), password));
            }

            name = null;
            password = null;
            return;
        }

        public void ViewDBS()
        {
            Console.WriteLine("Loaded databases:\n-------------------------\n");
            foreach (string s_name in mem_db_ref)
                Console.WriteLine("NAME: [" + s_name + "] CURRENT USED SLOT CAPACITY: [" + ((ArrayList)((ArrayList)mem_db[mem_db_ref.IndexOf(s_name)])[2]).Count + "] MAXIMUM SYSTEM SLOT CAPACITY: [" + ((ArrayList)((ArrayList)mem_db[mem_db_ref.IndexOf(s_name)])[2]).Capacity + "]");
            return;
        }

        public bool setDB(string name, object data, string tag, string subtag)
        {
            lock(mem_db) lock(mem_db_path) lock(mem_db_ref)
            {
                ArrayList al_tmp = (ArrayList)mem_db[mem_db_ref.IndexOf(name)];
                ((ArrayList)al_tmp[0]).Add(data);
                ((ArrayList)al_tmp[1]).Add(tag);
                ((ArrayList)al_tmp[2]).Add(subtag);
            }
            return true;
        }

        public ArrayList getDBAllNoThread(string db)
        {
            ArrayList data_handle = (ArrayList)((ArrayList)mem_db[mem_db_ref.IndexOf(db)])[0];
            return data_handle;
        }

        public readonly short OPCODE_GET = 0x00;
        public readonly short OPCODE_DELETE = 0x02;
        public ArrayList doDBSelectiveNoThread(string db, object data, string tag, string subtag, short OPCODE)
        {
            ArrayList returnable = new ArrayList();
            ArrayList subtag_handle = (ArrayList)((ArrayList)mem_db[mem_db_ref.IndexOf(db)])[2];
            ArrayList tag_handle = (ArrayList)((ArrayList)mem_db[mem_db_ref.IndexOf(db)])[1];
            ArrayList data_handle = (ArrayList)((ArrayList)mem_db[mem_db_ref.IndexOf(db)])[0];

            bool skip = false;
            int current = 0;
            //Get data by tag
            if (tag != S_NULL && tag != S_No)
            {
                while (current < K_DEFAULT_SLOT_SIZE)
                {
                    //Reset skip
                    skip = false;

                    //Get next index of occurrance
                    current = tag_handle.IndexOf(tag, current);

                    //Refine search with subtag if any. If not skip
                    if (subtag != S_NULL && subtag != S_No && current != -1)
                    {
                        //If the subtags do not match, do not include the result
                        if ((string)subtag_handle[current] != subtag)
                            skip = true;
                    }

                    //If there are no more occurances break the loop
                    if (current == -1)
                        break;

                    //If the value does not fit due to a wrong subtag skip and advance search
                    if (!skip)
                    {
                        if (OPCODE == OPCODE_GET)
                            returnable.Add( new ArrayList() { data_handle[current], tag_handle[current], skip == true ? null : subtag_handle[current]} );
                        else if (OPCODE == OPCODE_DELETE)
                        {
                            
                            lock(mem_db) lock(mem_db_path) lock(mem_db_ref)
                            {
                                ((ArrayList)((ArrayList)mem_db[mem_db_ref.IndexOf(db)])[0]).RemoveAt(current);
                                ((ArrayList)((ArrayList)mem_db[mem_db_ref.IndexOf(db)])[1]).RemoveAt(current);
                                ((ArrayList)((ArrayList)mem_db[mem_db_ref.IndexOf(db)])[2]).RemoveAt(current);
                            }
                        }
                    }
                    current++;
                }
            }
            //Get data by value
            else if (data != null && (string)data != S_NULL)
            {
                //ArrayList temp_tags = new ArrayList();
                int current_tag = 0;

                while (current < K_DEFAULT_SLOT_SIZE)
                {
                    current = data_handle.IndexOf(data, current);

                    //Gets the tag for the current data value and extracts all data related to that tag
                    //temp_tags.Add(tag_handle[current]);

                    //Get all data with the same tag
                    if (current == -1)
                        break;
                    //Get data with related tag
                    while (current_tag != -1)
                    {
                        //Find tag for the current data value
                        current_tag = tag_handle.IndexOf(tag_handle[current], current_tag);
                        if (current_tag == -1)
                            break;

                        if (OPCODE == OPCODE_GET)
                        {
                            //If index is not -1 or so the tag exists then add the value
                            //returnable.Add(data_handle[current_tag]);
                            returnable.Add( new ArrayList() { data_handle[current], current_tag, null } );
                        }
                        else if (OPCODE == OPCODE_DELETE)
                        {
                            
                            lock(mem_db) lock(mem_db_path) lock(mem_db_ref)
                            {
                                ((ArrayList)((ArrayList)mem_db[mem_db_ref.IndexOf(db)])[0]).RemoveAt(current_tag);
                                ((ArrayList)((ArrayList)mem_db[mem_db_ref.IndexOf(db)])[1]).RemoveAt(current_tag);
                                ((ArrayList)((ArrayList)mem_db[mem_db_ref.IndexOf(db)])[2]).RemoveAt(current_tag);
                            }
                        }
                        //Increment tag index so not to stay stuck on the preceeding one
                        current_tag++;
                    }

                    current++;
                }
            }
            return returnable;
        }
    }
}