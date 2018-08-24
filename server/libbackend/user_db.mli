open Core
open Libexecution
open Types
open Types.RuntimeT

(* DB struct functions *)
val cols_for : DbT.db -> (string * tipe) list

(* DB runtime functions *)
val set : state:exec_state -> magic:bool -> upsert:bool -> DbT.db -> string -> dval_map -> Uuidm.t
val get : state:exec_state -> magic:bool -> DbT.db -> string -> dval
val get_many : state:exec_state -> magic:bool -> DbT.db -> string list -> dval
val get_all : state:exec_state -> magic:bool -> DbT.db -> dval
val query : state:exec_state -> magic:bool -> DbT.db -> (string * dval) list -> dval
val query_by_one : state:exec_state -> magic:bool -> DbT.db -> string -> dval -> dval
val delete : state:exec_state -> DbT.db -> string -> unit
val delete_all : state:exec_state -> DbT.db -> unit
val count : DbT.db -> int

(* Deprecated: only used in legacy in deprecated libdb *)
val update : state:exec_state -> DbT.db -> dval_map -> unit
val coerce_key_value_pair_to_legacy_object : dval list -> dval
val coerce_dlist_of_kv_pairs_to_legacy_object : dval -> dval

(* DB schema modifications *)
val create : string -> tlid -> DbT.db
val add_col : id -> id -> DbT.db -> DbT.db
val set_col_name : id -> string -> DbT.db -> DbT.db
val set_col_type : id -> tipe -> DbT.db -> DbT.db
val change_col_name : id -> string -> DbT.db -> DbT.db
val change_col_type : id -> tipe -> DbT.db -> DbT.db
val initialize_migration : id -> id -> id -> DbT.migration_kind -> DbT.db -> DbT.db
val unlocked : Uuidm.t -> Uuidm.t -> DbT.db list -> DbT.db list
val db_locked : DbT.db -> bool

val find_db : DbT.db list -> string -> DbT.db option
val find_db_exn : DbT.db list -> string -> DbT.db

